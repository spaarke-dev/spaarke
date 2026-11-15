// GitHub #2845 — the per-request memo of the access evaluator's reads, and the concurrent per-type compositions.
//
// Every /api/v1/external request composed the caller three times (project, matter, work assignment), one after another, and
// each composition re-read the same subject-level answers. These tests pin the four contracts of the fix on the REAL
// AccessibleRecordSetService and NoAccessListReader orchestration, with only the wire reads substituted (counting doubles):
//   1. identical reads are made once per request (exact counts), and never across requests;
//   2. a fault, or an "unknown" answer a fail-closed veto is built from, is never remembered — even within the request;
//   3. a grant write (InvalidateGrantSetsAsync, which every grant writer calls) makes the next composition read again;
//   4. the three compositions run concurrently and build exactly the principal the sequential compositions build.

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
            store.Setup(s => s.ReadRemovedMarkerAsync(Contact, It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Interlocked.Increment(ref _removedMarkerReads);
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
            return Task.FromResult(Interlocked.Decrement(ref UnreadableContactStates) >= 0
                ? ContactRecordState.Unreadable
                : ContactRecordState.Active);
        }

        public override Task<ExternalGrantSet> GetGrantSetAsync(Guid contactId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _grantSetReads);
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

            return Task.FromResult(Grants);
        }

        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _orgReads);
            return Interlocked.Decrement(ref OrganizationMembershipFaults) >= 0
                ? Task.FromException<ActiveOrgMemberships>(new HttpRequestException("junction read faulted"))
                : Task.FromResult(new ActiveOrgMemberships(new[] { Firm }, new[] { Firm }, Unreadable: false));
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
    private sealed class CountingNoAccessReader : NoAccessListReader
    {
        private readonly ForwardingAccessor _accessor;
        private int _orgQueries, _recordQueries;

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

        public int UnreadableChunks;
        public int ThrowingChunks;

        internal override Task<List<NoAccessEntryRow>?> QueryChunkAsync(string subjectFilter, string objectFilter, CancellationToken ct)
        {
            if (objectFilter.Contains("_sprk_objectorganization_value", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _orgQueries);
                if (Interlocked.Decrement(ref ThrowingChunks) >= 0)
                {
                    return Task.FromException<List<NoAccessEntryRow>?>(new HttpRequestException("deny-list read faulted"));
                }

                if (Interlocked.Decrement(ref UnreadableChunks) >= 0)
                {
                    return Task.FromResult<List<NoAccessEntryRow>?>(null);
                }
            }
            else
            {
                Interlocked.Increment(ref _recordQueries);
            }

            return Task.FromResult<List<NoAccessEntryRow>?>(new List<NoAccessEntryRow>());
        }
    }
}
