// GitHub #2845 — the per-request memo of the access evaluator's reads, and the concurrent per-type compositions.
//
// Every /api/v1/external request composed the caller three times (project, matter, work assignment), one after another, and
// each composition re-read the same subject-level answers. These tests pin the four contracts of the fix on the REAL
// AccessibleRecordSetService and NoAccessListReader orchestration, with only the wire reads substituted (counting doubles):
//   1. identical reads are made once per request (exact counts), and never across requests;
//   2. a fault, or an "unknown" answer a fail-closed veto is built from, is never remembered — even within the request;
//   3. a grant write (InvalidateGrantSetsAsync, which every grant writer calls) makes the next composition read again;
//   4. the three compositions run concurrently and build exactly the principal the sequential compositions build;
//   5. two contacts composed in one request each get their own answers (every subject-level key carries the contact).

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;
using static Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public sealed class RequestAccessMemoTests
{
    private const string ProjectEntity = AccessibleRecordSetService.ProjectEntity;
    private const string MatterEntity = AccessibleRecordSetService.MatterEntity;
    private const string WorkAssignmentEntity = AccessibleRecordSetService.WorkAssignmentEntity;

    private static readonly Guid Contact = Guid.Parse("28450000-0000-4000-8000-0000000000c1");
    private static readonly Guid Firm = Guid.Parse("28450000-0000-4000-8000-0000000000f1");
    private static readonly Guid ProjectA = Guid.Parse("28450000-0000-4000-8000-0000000000a1");
    private static readonly Guid ProjectB = Guid.Parse("28450000-0000-4000-8000-0000000000a2");
    private static readonly Guid MatterA = Guid.Parse("28450000-0000-4000-8000-0000000000b1");
    private static readonly Guid WorkAssignmentA = Guid.Parse("28450000-0000-4000-8000-0000000000d1");

    // A second contact, composed in the same request as Contact (section 5).
    private static readonly Guid ContactB = Guid.Parse("28450000-0000-4000-8000-0000000000c2");
    private static readonly Guid FirmB = Guid.Parse("28450000-0000-4000-8000-0000000000f2");
    private static readonly Guid ProjectC = Guid.Parse("28450000-0000-4000-8000-0000000000a3");
    private static readonly Guid ProjectD = Guid.Parse("28450000-0000-4000-8000-0000000000a4");
    private static readonly Guid MatterB = Guid.Parse("28450000-0000-4000-8000-0000000000b2");
    private static readonly Guid WorkAssignmentB = Guid.Parse("28450000-0000-4000-8000-0000000000d2");

    // ─────────────────────────────────────────────────────────────────────
    // 1. Once per request — exact read counts for the CIAM principal's three compositions
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CiamPrincipal_ThreeCompositions_ReadEachSubjectLevelAnswerOnce_PerRequest()
    {
        var world = new World();
        var request = world.NewRequest();

        var composed = await CiamContactPrincipalStrategy.ComposeAsync(world.Service(request), Contact, "x@firm.example", "oid", CancellationToken.None);

        composed.Principal.ProjectAccess.Select(p => p.ProjectId).Should().BeEquivalentTo(new[] { ProjectA, ProjectB });
        composed.Principal.MatterAccess.Keys.Should().Equal(MatterA);
        composed.Principal.WorkAssignmentAccess.Keys.Should().Equal(WorkAssignmentA);

        // Subject-level answers: identical inputs in all three compositions → read once.
        world.Participations.ContactStateReads.Should().Be(1, "the contact's state is read once per request");
        world.RemovedMarkerReads.Should().Be(1, "the removed marker is read once per request (was 3)");
        world.Participations.GrantSetReads.Should().Be(1, "the grant set is read once per request (was 3)");
        world.Participations.OrganizationMembershipReads.Should().Be(1, "the organization memberships are read once per request (was 3)");
        world.NoAccess.OrganizationChunkQueries.Should().Be(1,
            "the deny-list query for the same subject and the same referenced organization is asked once (was 3)");

        // Per-type answers: different inputs in each composition → still one read each.
        world.Participations.FlagReads.Should().Be(3, "each type's own flags are read once");
        world.Participations.ReferencedOrganizationReads.Should().Be(3, "each type's own referenced organizations are read once");
        world.NoAccess.RecordChunkQueries.Should().Be(3, "each type's own per-record deny-list query is asked once");
    }

    [Fact]
    public async Task ANewRequest_ReadsEverythingAgain_NothingCrossesRequests()
    {
        var world = new World();

        await CiamContactPrincipalStrategy.ComposeAsync(world.Service(world.NewRequest()), Contact, null, null, CancellationToken.None);
        await CiamContactPrincipalStrategy.ComposeAsync(world.Service(world.NewRequest()), Contact, null, null, CancellationToken.None);

        world.Participations.ContactStateReads.Should().Be(2);
        world.RemovedMarkerReads.Should().Be(2);
        world.Participations.GrantSetReads.Should().Be(2);
        world.Participations.OrganizationMembershipReads.Should().Be(2);
        world.NoAccess.OrganizationChunkQueries.Should().Be(2);
    }

    [Fact]
    public async Task WithoutARequest_NoMemo_EveryCompositionReadsLive()
    {
        var world = new World();
        var service = world.Service(new FixedAccessor(null));

        await service.ComposeForCiamContactAsync(Contact, ProjectEntity, CancellationToken.None);
        await service.ComposeForCiamContactAsync(Contact, ProjectEntity, CancellationToken.None);

        world.RemovedMarkerReads.Should().Be(2, "a job or any call outside a request has no memo");
        world.Participations.GrantSetReads.Should().Be(2);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 2. A fault is never remembered — the next composition in the SAME request asks again
    // ─────────────────────────────────────────────────────────────────────

    public static TheoryData<string> FaultingSeams => new()
    {
        "removed-marker-throws", "removed-marker-unreadable", "org-memberships-throw", "grant-set-faulted",
        "no-access-chunk-unreadable", "no-access-chunk-throws", "contact-state-unreadable",
    };

    [Theory]
    [MemberData(nameof(FaultingSeams))]
    public async Task AFaultOnce_FailsClosed_ThenTheNextCompositionInTheSameRequestAsksAgain_AndGetsTheAnswer(string seam)
    {
        var world = new World();
        switch (seam)
        {
            case "removed-marker-throws": world.RemovedMarkerFaults = 1; break;
            case "removed-marker-unreadable": world.RemovedMarkerUnreadable = 1; break;
            case "org-memberships-throw": world.Participations.OrganizationMembershipFaults = 1; break;
            case "grant-set-faulted": world.Participations.FaultedGrantSets = 1; break;
            case "no-access-chunk-unreadable": world.NoAccess.UnreadableChunks = 1; break;
            case "no-access-chunk-throws": world.NoAccess.ThrowingChunks = 1; break;
            case "contact-state-unreadable": world.Participations.UnreadableContactStates = 1; break;
        }

        var service = world.Service(world.NewRequest());

        var first = await service.ComposeForCiamContactAsync(Contact, ProjectEntity, CancellationToken.None);
        var second = await service.ComposeForCiamContactAsync(Contact, ProjectEntity, CancellationToken.None);

        first.Rights.Should().NotContainKey(ProjectA, $"a {seam} fails closed on the composition that met it");
        second.Rights.Should().ContainKey(ProjectA, $"a {seam} is not remembered: the next composition asks again and is answered");

        var reads = seam switch
        {
            "removed-marker-throws" or "removed-marker-unreadable" => world.RemovedMarkerReads,
            "org-memberships-throw" => world.Participations.OrganizationMembershipReads,
            "grant-set-faulted" => world.Participations.GrantSetReads,
            "no-access-chunk-unreadable" or "no-access-chunk-throws" => world.NoAccess.OrganizationChunkQueries,
            _ => world.Participations.ContactStateReads,
        };
        reads.Should().Be(2, $"the {seam} read was asked again in the same request");

        // And the success IS remembered: a third composition reads nothing again.
        await service.ComposeForCiamContactAsync(Contact, ProjectEntity, CancellationToken.None);
        world.RemovedMarkerReads.Should().BeLessThanOrEqualTo(2);
        world.Participations.GrantSetReads.Should().BeLessThanOrEqualTo(2);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3. A write forgets — the composition after a grant write in the same request reads again
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterAGrantWrite_InTheSameRequest_TheNextCompositionReadsAgain()
    {
        var world = new World();
        var request = world.NewRequest();
        var service = world.Service(request);

        await service.ComposeForCiamContactAsync(Contact, ProjectEntity, CancellationToken.None);
        await service.ComposeForCiamContactAsync(Contact, ProjectEntity, CancellationToken.None);
        world.Participations.GrantSetReads.Should().Be(1, "precondition: the second composition was answered from the memo");

        // Every grant writer calls the ONE invalidation routine after its write (task 137).
        await world.Participations.InvalidateGrantSetsAsync(new[] { Contact }, Array.Empty<Guid>(), CancellationToken.None);
        world.Participations.Grants = GrantSet(projects: new[] { ProjectA }); // the write revoked ProjectB

        var after = await service.ComposeForCiamContactAsync(Contact, ProjectEntity, CancellationToken.None);

        after.Rights.Keys.Should().Equal(ProjectA);
        world.Participations.GrantSetReads.Should().Be(2, "the grant set is read again after the write");
        world.Participations.ContactStateReads.Should().Be(2);
        world.RemovedMarkerReads.Should().Be(2);
        world.Participations.OrganizationMembershipReads.Should().Be(2);
        world.NoAccess.OrganizationChunkQueries.Should().Be(2);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 4. Concurrency — the three compositions run at once, and build the sequential principal
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CiamPrincipal_TheThreeCompositionsRunConcurrently_AndBuildTheSequentialPrincipal()
    {
        // Each composition's flag read waits until all three have started. Composed one after another, the first would wait
        // for the others forever (the double times out and the composition faults).
        var world = new World();
        world.Participations.FlagBarrier = new Barrier3();

        var concurrent = (await CiamContactPrincipalStrategy.ComposeAsync(
            world.Service(world.NewRequest()), Contact, "x@firm.example", "oid", CancellationToken.None)).Principal;

        world.Participations.FlagBarrier.Arrived.Should().Be(3);

        // The reference: each composition awaited alone, in order, in a fresh request.
        var sequentialWorld = new World();
        var service = sequentialWorld.Service(sequentialWorld.NewRequest());
        var projects = await service.ComposeForCiamContactAsync(Contact, ProjectEntity, CancellationToken.None);
        var matters = await service.ComposeForCiamContactAsync(Contact, MatterEntity, CancellationToken.None);
        var workAssignments = await service.ComposeForCiamContactAsync(Contact, WorkAssignmentEntity, CancellationToken.None);

        concurrent.Plane.Should().Be(CallerPrincipalPlane.CiamContact);
        concurrent.ContactId.Should().Be(Contact);
        concurrent.ProjectAccess.Should().BeEquivalentTo(
            CallerPrincipal.FromReadBearing(projects.Rights).Select(kvp => new CallerProjectAccess { ProjectId = kvp.Key, Rights = kvp.Value }),
            o => o.WithStrictOrdering());
        concurrent.MatterAccess.Should().Equal(CallerPrincipal.FromReadBearing(matters.Rights));
        concurrent.WorkAssignmentAccess.Should().Equal(CallerPrincipal.FromReadBearing(workAssignments.Rights));
        concurrent.ProjectAccess.Should().HaveCount(2, "the world grants two projects");
    }

    [Fact]
    public async Task WorkforcePrincipal_TheThreeCompositionsRunConcurrently_InTheirOrder()
    {
        var principal = WorkforcePrincipalResolution.ForContact(Contact, "oid", "tenant").Principal!;
        var started = new ConcurrentQueue<string>();
        var barrier = new Barrier3();
        var sets = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        sets.Setup(s => s.ComposeAsync(principal, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (WorkforcePrincipal _, string entity, CancellationToken _) =>
            {
                started.Enqueue(entity);
                await barrier.ArriveAndWaitAsync();
                return new AccessibleRecordSet
                {
                    PrincipalKind = WorkforcePrincipalKind.ContactOnly,
                    EntityType = entity,
                    Rights = new Dictionary<Guid, AccessRights> { [ProjectA] = AccessRights.Read },
                    Sources = new AccessibleRecordSetSources(false, true, false),
                };
            });

        var composed = await WorkforcePrincipalStrategy.ComposeAsync(sets.Object, principal, null, CancellationToken.None);

        started.Should().Equal(ProjectEntity, MatterEntity, WorkAssignmentEntity);
        composed.Projects.EntityType.Should().Be(ProjectEntity);
        composed.Matters.EntityType.Should().Be(MatterEntity);
        composed.WorkAssignments.EntityType.Should().Be(WorkAssignmentEntity);
    }

    [Fact]
    public async Task WorkforcePrincipal_SeveralCompositionsFault_TheFirstInProjectMatterWorkAssignmentOrderIsThrown()
    {
        // As the sequential awaits did: the project composition's fault wins even when it arrives last.
        var principal = WorkforcePrincipalResolution.ForContact(Contact, "oid", "tenant").Principal!;
        var sets = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        sets.Setup(s => s.ComposeAsync(principal, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (WorkforcePrincipal _, string entity, CancellationToken _) =>
            {
                await Task.Delay(entity == ProjectEntity ? 200 : 0);
                throw new InvalidOperationException(entity);
            });

        var act = () => WorkforcePrincipalStrategy.ComposeAsync(sets.Object, principal, null, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(ProjectEntity);
    }

    // ─────────────────────────────────────────────────────────────────────
    // The memo itself
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Memo_ACallerWaitingOnAReadThatFaults_AsksAgainItself()
    {
        var memo = new RequestAccessMemo();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;

        var first = memo.GetOrReadAsync<int>("k", async _ =>
        {
            Interlocked.Increment(ref reads);
            await release.Task;
            throw new HttpRequestException("faulted");
        }, _ => true, CancellationToken.None);
        var waiter = memo.GetOrReadAsync<int>("k", _ =>
        {
            Interlocked.Increment(ref reads);
            return Task.FromResult(42);
        }, _ => true, CancellationToken.None);

        release.SetResult();

        await FluentActions.Awaiting(() => first).Should().ThrowAsync<HttpRequestException>();
        (await waiter).Should().Be(42, "the waiter did not inherit the fault; it read again");
        reads.Should().Be(2);
        memo.Count.Should().Be(1, "only the answer is remembered");
    }

    [Fact]
    public async Task Memo_ACallerWaitingOnAReadWhoseAnswerIsRejected_AsksAgainItself()
    {
        // The owner's read is still open when the waiter joins the same key; it then answers an "unknown" value the
        // caller's remember() refuses. The waiter must not be served that answer: it reads again itself.
        var memo = new RequestAccessMemo();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        static bool Remember(RemovedMarkerRead marker) => marker is RemovedMarkerRead.NotRemoved or RemovedMarkerRead.Removed;

        var owner = memo.GetOrReadAsync(RequestAccessMemo.RemovedMarkerKey(Contact), async _ =>
        {
            Interlocked.Increment(ref reads);
            await release.Task;
            return RemovedMarkerRead.Unreadable;
        }, Remember, CancellationToken.None);
        var waiter = memo.GetOrReadAsync(RequestAccessMemo.RemovedMarkerKey(Contact), _ =>
        {
            Interlocked.Increment(ref reads);
            return Task.FromResult(RemovedMarkerRead.NotRemoved);
        }, Remember, CancellationToken.None);

        waiter.IsCompleted.Should().BeFalse("precondition: the waiter joined the owner's read while it was in flight");
        release.SetResult();

        (await owner).Should().Be(RemovedMarkerRead.Unreadable, "the owner is answered with its own read");
        (await waiter).Should().Be(RemovedMarkerRead.NotRemoved, "the waiter did not inherit the rejected answer; it read again");
        reads.Should().Be(2);
        memo.Count.Should().Be(1, "only the proven answer is remembered");
    }

    [Fact]
    public async Task CiamPrincipal_AnUnknownDenyChunkAnswerMetByOneConcurrentComposition_FailsOnlyThatOneClosed()
    {
        // The reference: the same world with no fault.
        var referenceWorld = new World();
        var reference = (await CiamContactPrincipalStrategy.ComposeAsync(
            referenceWorld.Service(referenceWorld.NewRequest()), Contact, null, null, CancellationToken.None)).Principal;
        reference.ProjectAccess.Should().NotBeEmpty();
        reference.MatterAccess.Should().NotBeEmpty();
        reference.WorkAssignmentAccess.Should().NotBeEmpty();

        // The three compositions run at once (the flag barrier proves it). The ethical-wall query they share answers
        // "unknown" (null) ONCE, and that read is held open until all three deny-list evaluations have joined it, so two
        // compositions are WAITING on the read whose answer is rejected.
        var world = new World();
        world.Participations.FlagBarrier = new Barrier3();
        world.NoAccess.UnreadableChunks = 1;
        world.NoAccess.HoldUnreadableChunkUntilThreeEvaluations = true;

        var composed = (await CiamContactPrincipalStrategy.ComposeAsync(
            world.Service(world.NewRequest()), Contact, null, null, CancellationToken.None)).Principal;

        world.NoAccess.EvaluationsStarted.Should().Be(3, "precondition: all three evaluations joined the held read");
        world.NoAccess.OrganizationChunkQueries.Should().Be(2,
            "the read that answered unknown was asked again once, by a waiter, and its proven answer shared");

        var failedClosed = new[]
        {
            composed.ProjectAccess.Count == 0, composed.MatterAccess.Count == 0, composed.WorkAssignmentAccess.Count == 0,
        };
        failedClosed.Count(f => f).Should().Be(1, "only the composition that met the unknown answer fails closed");
        if (!failedClosed[0])
        {
            composed.ProjectAccess.Should().BeEquivalentTo(reference.ProjectAccess, o => o.WithStrictOrdering());
        }

        if (!failedClosed[1])
        {
            composed.MatterAccess.Should().Equal(reference.MatterAccess);
        }

        if (!failedClosed[2])
        {
            composed.WorkAssignmentAccess.Should().Equal(reference.WorkAssignmentAccess);
        }
    }

    [Fact]
    public async Task Memo_ARejectedAnswerIsNotShared_AndClearForgetsEverything()
    {
        var memo = new RequestAccessMemo();
        var reads = 0;
        Task<string> Read(string value) => memo.GetOrReadAsync("k", _ =>
        {
            reads++;
            return Task.FromResult(value);
        }, v => v != "unknown", CancellationToken.None);

        (await Read("unknown")).Should().Be("unknown");
        (await Read("known")).Should().Be("known", "an unknown answer was not remembered");
        (await Read("other")).Should().Be("known", "a known answer is");
        reads.Should().Be(2);

        memo.Clear();
        (await Read("after-write")).Should().Be("after-write");
        reads.Should().Be(3);
    }

    [Fact]
    public void Memo_IsOnePerHttpContext_AndNoneWithoutOne()
    {
        var a = new DefaultHttpContext();
        RequestAccessMemo.For(a).Should().BeSameAs(RequestAccessMemo.For(a));
        RequestAccessMemo.For(a).Should().NotBeSameAs(RequestAccessMemo.For(new DefaultHttpContext()));
        RequestAccessMemo.For(null).Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 5. Two contacts in ONE request — the second contact is never served the first one's answers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The real path: AssignTaskRecipientGate.ComposePortalPrincipalAsync (Services/Signals/AssignTaskRecipientGate.cs)
    /// composes a SECOND contact — the task recipient — inside an internal request, so the recipient's compositions share
    /// that request's memo. Contact A, then contact B, are composed in the SAME HttpContext; they differ in grant set,
    /// organization memberships and deny-list rows, and (first case) in removed marker.
    /// </summary>
    /// <remarks>Two cases, because a removed contact's composition stops at its marker (it reads no grant set, no
    /// memberships, no deny rows). With B removed, the case proves the marker answer is per contact; with both readable, it
    /// proves the grant set, the memberships and the deny-list queries are.</remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TwoContactsComposedInOneRequest_TheSecondGetsItsOwnAnswers_AndEachReadHappensOncePerContact(bool contactBRemoved)
    {
        var world = new World();
        world.RemovedMarkerByContact[ContactB] = contactBRemoved ? RemovedMarkerRead.Removed : RemovedMarkerRead.NotRemoved;
        world.Participations.GrantsByContact[ContactB] = new ExternalGrantSet
        {
            Projects = new[] { ProjectC, ProjectD }
                .Select(id => new ExternalParticipation { ProjectId = id, AccessLevel = ExternalAccessLevel.ViewOnly }).ToList(),
            MatterGrants = RootGrants(ExternalAccessLevel.FullAccess, MatterB),
            WorkAssignmentGrants = RootGrants(ExternalAccessLevel.ViewOnly, WorkAssignmentB),
        };
        world.Participations.OrganizationByContact[ContactB] = FirmB;

        // A's row walls contact A off ProjectB; B's row walls B's ORGANIZATION off ProjectD (so B's memberships matter).
        world.NoAccess.Rows.Add(DenyRecord(subjectContact: Contact, subjectOrganization: null, ProjectB));
        world.NoAccess.Rows.Add(DenyRecord(subjectContact: null, subjectOrganization: FirmB, ProjectD));

        var service = world.Service(world.NewRequest()); // ONE request (one HttpContext, one memo) for both contacts

        var a = (await CiamContactPrincipalStrategy.ComposeAsync(service, Contact, null, null, CancellationToken.None)).Principal;
        var b = (await CiamContactPrincipalStrategy.ComposeAsync(service, ContactB, null, null, CancellationToken.None)).Principal;

        a.ProjectAccess.Select(p => p.ProjectId).Should().Equal(new[] { ProjectA }, "A's own grants, less A's own deny row");
        a.MatterAccess.Keys.Should().Equal(MatterA);
        a.WorkAssignmentAccess.Keys.Should().Equal(WorkAssignmentA);

        if (contactBRemoved)
        {
            b.ProjectAccess.Should().BeEmpty("B's own marker says removed; A's 'not removed' is never B's answer");
            b.MatterAccess.Should().BeEmpty();
            b.WorkAssignmentAccess.Should().BeEmpty();
        }
        else
        {
            b.ProjectAccess.Select(p => p.ProjectId).Should().Equal(new[] { ProjectC },
                "B's own grants (never A's), less the row walling B's own organization (never A's) off ProjectD");
            b.MatterAccess.Keys.Should().Equal(MatterB);
            b.WorkAssignmentAccess.Keys.Should().Equal(WorkAssignmentB);
        }

        // Each read once per contact: A's three compositions read A's answers once; B's read B's own once (none past the
        // marker when B is removed).
        var bReadsPastMarker = contactBRemoved ? 0 : 1;
        world.Participations.ContactStateReadsFor(Contact).Should().Be(1);
        world.Participations.ContactStateReadsFor(ContactB).Should().Be(1);
        world.RemovedMarkerReadsFor(Contact).Should().Be(1);
        world.RemovedMarkerReadsFor(ContactB).Should().Be(1, "B's marker is read for B, not served from A's");
        world.Participations.GrantSetReadsFor(Contact).Should().Be(1);
        world.Participations.GrantSetReadsFor(ContactB).Should().Be(bReadsPastMarker, "B's grant set is read for B, not served from A's");
        world.Participations.OrganizationMembershipReadsFor(Contact).Should().Be(1);
        world.Participations.OrganizationMembershipReadsFor(ContactB).Should().Be(bReadsPastMarker);
        world.NoAccess.OrganizationChunkQueriesFor(Contact).Should().Be(1);
        world.NoAccess.OrganizationChunkQueriesFor(ContactB).Should().Be(bReadsPastMarker);
    }

    private static NoAccessEntryRow DenyRecord(Guid? subjectContact, Guid? subjectOrganization, Guid record) => new()
    {
        sprk_noaccessentryid = Guid.NewGuid(),
        _sprk_subjectcontact_value = subjectContact,
        _sprk_subjectorganization_value = subjectOrganization,
        _sprk_objectrecordtype_value = Guid.Parse("28450000-0000-4000-8000-0000000000e1"),
        sprk_objectrecordid = record.ToString("D"),
    };

    // ─────────────────────────────────────────────────────────────────────
    // The world: the real composer and deny-list orchestration over counting wire doubles
    // ─────────────────────────────────────────────────────────────────────

    private static ExternalGrantSet GrantSet(Guid[] projects) => new()
    {
        Projects = projects.Select(id => new ExternalParticipation { ProjectId = id, AccessLevel = ExternalAccessLevel.Collaborate }).ToList(),
        MatterGrants = RootGrants(ExternalAccessLevel.ViewOnly, MatterA),
        WorkAssignmentGrants = RootGrants(ExternalAccessLevel.FullAccess, WorkAssignmentA),
    };

    private sealed class World
    {
        private int _removedMarkerReads;

        public World()
        {
            Participations = new CountingParticipations();
            NoAccess = new CountingNoAccessReader();
        }

        public FixedAccessor Accessor { get; } = new(null);

        public CountingParticipations Participations { get; }

        public CountingNoAccessReader NoAccess { get; }

        public int RemovedMarkerReads => Volatile.Read(ref _removedMarkerReads);

        public int RemovedMarkerFaults;

        public int RemovedMarkerUnreadable;

        /// <summary>The removed marker of each contact other than <see cref="Contact"/> (section 5).</summary>
        public ConcurrentDictionary<Guid, RemovedMarkerRead> RemovedMarkerByContact { get; } = new();

        private readonly ConcurrentDictionary<Guid, int> _removedMarkerReadsByContact = new();

        public int RemovedMarkerReadsFor(Guid contactId) => _removedMarkerReadsByContact.GetValueOrDefault(contactId);

        /// <summary>A new request: a fresh HttpContext (and so a fresh memo) behind the shared accessor.</summary>
        public FixedAccessor NewRequest()
        {
            Accessor.HttpContext = new DefaultHttpContext();
            return Accessor;
        }

        public AccessibleRecordSetService Service(IHttpContextAccessor accessor)
        {
            Participations.SetAccessor(accessor);
            NoAccess.SetAccessor(accessor);

            var store = new Mock<IContactIdentityStore>(MockBehavior.Strict);
            store.Setup(s => s.ReadRemovedMarkerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid contactId, CancellationToken _) =>
                {
                    Interlocked.Increment(ref _removedMarkerReads);
                    _removedMarkerReadsByContact.AddOrUpdate(contactId, 1, (_, n) => n + 1);
                    if (contactId != Contact)
                    {
                        return RemovedMarkerByContact.TryGetValue(contactId, out var marker)
                            ? Task.FromResult(marker)
                            : Task.FromException<RemovedMarkerRead>(new InvalidOperationException($"No marker for contact {contactId}."));
                    }

                    if (Interlocked.Decrement(ref RemovedMarkerFaults) >= 0)
                    {
                        return Task.FromException<RemovedMarkerRead>(new HttpRequestException("marker read faulted"));
                    }

                    return Task.FromResult(Interlocked.Decrement(ref RemovedMarkerUnreadable) >= 0
                        ? RemovedMarkerRead.Unreadable
                        : RemovedMarkerRead.NotRemoved);
                });

            return new AccessibleRecordSetService(
                new Mock<IMembershipResolverService>(MockBehavior.Strict).Object,
                Participations,
                new Mock<ISubjectStandingGrantReader>(MockBehavior.Strict).Object,
                NoAccess,
                store.Object,
                InternalSystemUsers(),
                NoFilingEntities(),
                NullLogger<AccessibleRecordSetService>.Instance,
                httpContextAccessor: accessor);
        }
    }

    /// <summary>An accessor whose context is a plain field (the framework's lives in an AsyncLocal).</summary>
    private sealed class FixedAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    /// <summary>The accessor the doubles were built with, re-pointed per test step.</summary>
    private sealed class ForwardingAccessor : IHttpContextAccessor
    {
        public IHttpContextAccessor? Target { get; set; }

        public HttpContext? HttpContext
        {
            get => Target?.HttpContext;
            set => throw new NotSupportedException();
        }
    }

    /// <summary>Releases its callers once three have arrived; a caller left waiting alone times out (sequential code).</summary>
    private sealed class Barrier3
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public int Arrived => Volatile.Read(ref _arrived);

        public Task ArriveAndWaitAsync()
        {
            if (Interlocked.Increment(ref _arrived) == 3)
            {
                _all.TrySetResult();
            }

            return _all.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class CountingParticipations : ExternalParticipationService
    {
        private readonly ForwardingAccessor _accessor;
        private int _contactStateReads, _grantSetReads, _orgReads, _flagReads, _refReads;

        public CountingParticipations()
            : this(new ForwardingAccessor())
        {
        }

        private CountingParticipations(ForwardingAccessor accessor)
            : base(new HttpClient(), Mock.Of<ITenantCache>(), new ConfigurationBuilder().Build(), credential: null!, accessor,
                   NullLogger<ExternalParticipationService>.Instance, NoFilingEntities())
            => _accessor = accessor;

        public void SetAccessor(IHttpContextAccessor accessor) => _accessor.Target = accessor;

        public ExternalGrantSet Grants { get; set; } = GrantSet(projects: new[] { ProjectA, ProjectB });

        /// <summary>The grant set of each contact other than <see cref="Contact"/> (section 5).</summary>
        public ConcurrentDictionary<Guid, ExternalGrantSet> GrantsByContact { get; } = new();

        /// <summary>The one organization of each contact other than <see cref="Contact"/> (section 5); Contact's is Firm.</summary>
        public ConcurrentDictionary<Guid, Guid> OrganizationByContact { get; } = new();

        private readonly ConcurrentDictionary<Guid, int> _contactStateReadsBy = new(), _grantSetReadsBy = new(), _orgReadsBy = new();

        public int ContactStateReadsFor(Guid contactId) => _contactStateReadsBy.GetValueOrDefault(contactId);
        public int GrantSetReadsFor(Guid contactId) => _grantSetReadsBy.GetValueOrDefault(contactId);
        public int OrganizationMembershipReadsFor(Guid contactId) => _orgReadsBy.GetValueOrDefault(contactId);

        public Barrier3? FlagBarrier { get; set; }

        public int ContactStateReads => Volatile.Read(ref _contactStateReads);
        public int GrantSetReads => Volatile.Read(ref _grantSetReads);
        public int OrganizationMembershipReads => Volatile.Read(ref _orgReads);
        public int FlagReads => Volatile.Read(ref _flagReads);
        public int ReferencedOrganizationReads => Volatile.Read(ref _refReads);

        public int OrganizationMembershipFaults;
        public int FaultedGrantSets;
        public int UnreadableContactStates;

        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct)
        {
            Interlocked.Increment(ref _contactStateReads);
            _contactStateReadsBy.AddOrUpdate(contactId, 1, (_, n) => n + 1);
            return Task.FromResult(Interlocked.Decrement(ref UnreadableContactStates) >= 0
                ? ContactRecordState.Unreadable
                : ContactRecordState.Active);
        }

        public override Task<ExternalGrantSet> GetGrantSetAsync(Guid contactId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _grantSetReads);
            _grantSetReadsBy.AddOrUpdate(contactId, 1, (_, n) => n + 1);
            if (Interlocked.Decrement(ref FaultedGrantSets) >= 0)
            {
                // A faulted read: what was read is nothing; the set says it is faulted (never cached, task 132).
                return Task.FromResult(new ExternalGrantSet
                {
                    Projects = Array.Empty<ExternalParticipation>(),
                    MatterGrants = NoRootGrants,
                    WorkAssignmentGrants = NoRootGrants,
                    Faulted = true,
                });
            }

            return Task.FromResult(GrantsByContact.TryGetValue(contactId, out var own) ? own : Grants);
        }

        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _orgReads);
            _orgReadsBy.AddOrUpdate(contactId, 1, (_, n) => n + 1);
            var organization = OrganizationByContact.TryGetValue(contactId, out var own) ? own : Firm;
            return Interlocked.Decrement(ref OrganizationMembershipFaults) >= 0
                ? Task.FromException<ActiveOrgMemberships>(new HttpRequestException("junction read faulted"))
                : Task.FromResult(new ActiveOrgMemberships(new[] { organization }, new[] { organization }, Unreadable: false));
        }

        public override async Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _flagReads);
            if (FlagBarrier is { } barrier)
            {
                await barrier.ArriveAndWaitAsync();
            }

            return recordIds.Distinct().ToDictionary(id => id, _ => RootRecordFlags.None);
        }

        public override Task<IReadOnlyDictionary<Guid, ReferencedOrganizations>> GetReferencedOrganizationIdsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _refReads);
            IReadOnlyDictionary<Guid, ReferencedOrganizations> answer = recordIds.Distinct()
                .ToDictionary(id => id, _ => new ReferencedOrganizations(new[] { Firm }, Unreadable: false));
            return Task.FromResult(answer);
        }
    }

    /// <summary>The REAL deny-list orchestration (chunking, both loops, fail-closed) over a counting wire read.</summary>
    /// <remarks>It re-implements <see cref="INoAccessListReader"/> only to count the evaluations that have REACHED their first
    /// query: the real <see cref="NoAccessListReader.GetDeniedRecordsAsync(NoAccessSubjects, IReadOnlyCollection{NoAccessCandidateRecord}, CancellationToken)"/>
    /// runs synchronously up to its first unfinished await — the memo join of its first query — so when it returns its task,
    /// that evaluation either owns that read or is waiting on it.</remarks>
    private sealed class CountingNoAccessReader : NoAccessListReader, INoAccessListReader
    {
        private readonly ForwardingAccessor _accessor;
        private readonly TaskCompletionSource _threeEvaluationsJoined = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _orgQueries, _recordQueries, _evaluations;

        public CountingNoAccessReader()
            : this(new ForwardingAccessor())
        {
        }

        private CountingNoAccessReader(ForwardingAccessor accessor)
            : base(new HttpClient(), configuration: null!, credential: null!, NullLogger<NoAccessListReader>.Instance, accessor)
            => _accessor = accessor;

        public void SetAccessor(IHttpContextAccessor accessor) => _accessor.Target = accessor;

        public int OrganizationChunkQueries => Volatile.Read(ref _orgQueries);
        public int RecordChunkQueries => Volatile.Read(ref _recordQueries);

        public int EvaluationsStarted => Volatile.Read(ref _evaluations);

        public int UnreadableChunks;
        public int ThrowingChunks;

        /// <summary>The deny-list rows in Dataverse; a query answers those its subject and object filters name.</summary>
        public ConcurrentBag<NoAccessEntryRow> Rows { get; } = new();

        private readonly ConcurrentDictionary<string, int> _orgQueriesBySubject = new(StringComparer.Ordinal);

        /// <summary>The organization-object queries whose subject filter names <paramref name="contactId"/>.</summary>
        public int OrganizationChunkQueriesFor(Guid contactId) => _orgQueriesBySubject
            .Where(kv => kv.Key.Contains($"_sprk_subjectcontact_value eq {contactId}", StringComparison.Ordinal))
            .Sum(kv => kv.Value);

        /// <summary>When set, an unreadable chunk's answer is held until three evaluations have joined their first query.</summary>
        public bool HoldUnreadableChunkUntilThreeEvaluations { get; set; }

        Task<NoAccessListResult> INoAccessListReader.GetDeniedRecordsAsync(
            Guid? contactId, IReadOnlyCollection<Guid> organizationIds, IReadOnlyCollection<NoAccessCandidateRecord> candidates,
            CancellationToken ct)
            => Started(GetDeniedRecordsAsync(contactId, organizationIds, candidates, ct));

        Task<NoAccessListResult> INoAccessListReader.GetDeniedRecordsAsync(
            NoAccessSubjects subjects, IReadOnlyCollection<NoAccessCandidateRecord> candidates, CancellationToken ct)
            => Started(GetDeniedRecordsAsync(subjects, candidates, ct));

        private Task<NoAccessListResult> Started(Task<NoAccessListResult> evaluation)
        {
            if (Interlocked.Increment(ref _evaluations) == 3)
            {
                _threeEvaluationsJoined.TrySetResult();
            }

            return evaluation;
        }

        internal override Task<List<NoAccessEntryRow>?> QueryChunkAsync(string subjectFilter, string objectFilter, CancellationToken ct)
        {
            if (objectFilter.Contains("_sprk_objectorganization_value", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _orgQueries);
                _orgQueriesBySubject.AddOrUpdate(subjectFilter, 1, (_, n) => n + 1);
                if (Interlocked.Decrement(ref ThrowingChunks) >= 0)
                {
                    return Task.FromException<List<NoAccessEntryRow>?>(new HttpRequestException("deny-list read faulted"));
                }

                if (Interlocked.Decrement(ref UnreadableChunks) >= 0)
                {
                    return HoldUnreadableChunkUntilThreeEvaluations
                        ? UnreadableOnceThreeJoinedAsync()
                        : Task.FromResult<List<NoAccessEntryRow>?>(null);
                }
            }
            else
            {
                Interlocked.Increment(ref _recordQueries);
            }

            return Task.FromResult<List<NoAccessEntryRow>?>(Rows.Where(row => Names(subjectFilter, objectFilter, row)).ToList());
        }

        /// <summary>Whether the query's filters select <paramref name="row"/> (the subject and object fragments only).</summary>
        private static bool Names(string subjectFilter, string objectFilter, NoAccessEntryRow row)
        {
            var subject = (row._sprk_subjectcontact_value is { } contact
                           && subjectFilter.Contains($"_sprk_subjectcontact_value eq {contact}", StringComparison.Ordinal))
                          || (row._sprk_subjectorganization_value is { } organization
                              && subjectFilter.Contains($"_sprk_subjectorganization_value eq {organization}", StringComparison.Ordinal));
            var obj = (row.sprk_objectrecordid is { } record
                       && objectFilter.Contains($"sprk_objectrecordid eq '{record}'", StringComparison.Ordinal))
                      || (row._sprk_objectorganization_value is { } objectOrganization
                          && objectFilter.Contains($"_sprk_objectorganization_value eq {objectOrganization}", StringComparison.Ordinal));
            return subject && obj;
        }

        private async Task<List<NoAccessEntryRow>?> UnreadableOnceThreeJoinedAsync()
        {
            await _threeEvaluationsJoined.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return null;
        }
    }
}
