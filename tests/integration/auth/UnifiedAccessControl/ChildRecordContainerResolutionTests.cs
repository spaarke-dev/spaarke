using System.ServiceModel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using NSubstitute;
using NSubstitute.Core;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 155 (#1080) — content filed to a CHILD record (to-do, event, invoice) lands in
/// the container of its SECURE root, or nowhere. Owner C10 part 2 (binding): every child of a secure record is
/// secure.
///
/// <para><b>What was wrong.</b> <c>RecordContainerResolver</c> returned the non-secure fallback for every
/// non-securable entity WITHOUT reading the record. On the record-keyed upload routes (no fallback) that was
/// Unresolved — the misleading "No storage container is configured" 409 for a to-do whose business unit had a
/// container. On the Office save path it fell through to a SHARED default container, so a to-do under a secure
/// project put that project's content where SPE cannot un-share it (the #1038 class, reached through the child).</para>
///
/// <para><b>The one REAL registry rule, simulated faithfully.</b> Every double here answers classification through
/// <see cref="TestEntityCatalog"/>, the shared model of the production rule. The world mirrors live dev
/// (2026-10-01): project / matter / work assignment / invoice carry <c>sprk_issecure</c>; service request does not.</para>
/// </summary>
public class ChildRecordContainerResolutionTests
{
    private const string RootContainer = "b!secure-root-container-0000000000";
    private const string OtherRootContainer = "b!other-secure-root-container-0000";
    private const string BusinessUnitContainer = "b!child-bu-container-00000000000000";
    private const string ArchiveContainer = "b!archive-container-000000000000000";

    private static readonly Guid ChildId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ProjectId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid MatterId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid ServiceRequestId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid CommunicationId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid BusinessUnitId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // ============================================================================================
    // The positive cases — the acceptance criteria
    // ============================================================================================

    [Fact(DisplayName = "Task 155: a to-do under a SECURE project resolves to the PROJECT's own container; its business unit is never read")]
    public async Task Todo_UnderASecureProject_ResolvesTheProjectsOwnContainer()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedSecure);
        decision.ContainerId.Should().Be(RootContainer,
            "a child of a secure record is secure (owner C10 part 2) — its content belongs with the root");

        // A usable shared container must never be in scope when a secure root decides.
        world.Reads("businessunit").Should().Be(0);
    }

    [Theory(DisplayName = "Task 155: the same holds by ALIAS (the route value), for to-do and event")]
    [InlineData("todo", "sprk_todo")]
    [InlineData("event", "sprk_event")]
    public async Task Child_NamedByAlias_UnderASecureProject_ResolvesTheProjectsOwnContainer(string alias, string logical)
    {
        var world = new World()
            .WithChild(logical, regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync(alias, ChildId);

        decision.ContainerId.Should().Be(RootContainer);
    }

    [Fact(DisplayName = "Task 155: a to-do under a NON-secure project resolves its OWN business-unit container — no 409")]
    public async Task Todo_UnderANonSecureProject_ResolvesItsOwnBusinessUnitContainer()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: false, "b!a-stale-project-stamp-00000000000")
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedFallback,
            "Unresolved here is the misleading 'No storage container is configured' 409 this task removes");
        decision.ContainerId.Should().Be(BusinessUnitContainer,
            "a non-secure root's own stamp is never read as the child's container");
    }

    [Fact(DisplayName = "Task 155: an event with NO ancestor resolves its business-unit container")]
    public async Task Event_WithNoAncestor_ResolvesItsBusinessUnitContainer()
    {
        var world = new World()
            .WithChild("sprk_event")
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_event", ChildId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedFallback);
        decision.ContainerId.Should().Be(BusinessUnitContainer);
        world.RootReads().Should().Be(0, "no link, nothing to read");
    }

    [Fact(DisplayName = "Task 155: a business unit with NO container leaves a root-less child Unresolved (the documented 409)")]
    public async Task Event_WithNoAncestor_AndABusinessUnitWithoutContainer_IsUnresolved()
    {
        var world = new World()
            .WithChild("sprk_event")
            .WithBusinessUnit(container: null);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_event", ChildId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.Unresolved);
        decision.ContainerId.Should().BeNull();
    }

    [Fact(DisplayName = "Task 155: a SECURE root and a NON-secure root — the secure one decides")]
    public async Task Todo_WithOneSecureAndOneNonSecureRoot_ResolvesTheSecureRoot()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId, regardingMatter: MatterId)
            .WithRoot("sprk_project", ProjectId, isSecure: false, containerId: null)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        decision.ContainerId.Should().Be(RootContainer,
            "the shared container is a disclosure; the secure one is merely conservative");
    }

    [Fact(DisplayName = "Task 155: a root whose entity CANNOT be secure (service request) is never read")]
    public async Task Todo_UnderAServiceRequest_DoesNotReadIt_AndResolvesTheBusinessUnit()
    {
        // Live dev: sprk_servicerequest carries no sprk_issecure, so reading the flag would FAULT. Derived from
        // metadata, so the day it gains the flag it is read without a code change.
        var world = new World()
            .WithChild("sprk_todo", regardingServiceRequest: ServiceRequestId)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        decision.ContainerId.Should().Be(BusinessUnitContainer);
        world.Reads("sprk_servicerequest").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155: an invoice (securable, own flag FALSE) under a secure matter takes the matter's container through its TYPED lookup")]
    public async Task SecurableChild_NotItselfSecure_UnderASecureMatter_ResolvesTheMattersContainer()
    {
        // Live dev: sprk_invoice carries sprk_issecure, and links to its root through typed sprk_matter /
        // sprk_project lookups, not sprk_regarding{core}.
        var world = new World(securable: ["sprk_project", "sprk_matter", "sprk_workassignment", "sprk_invoice"])
            .WithChild("sprk_invoice", typedMatter: MatterId, isSecure: false)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("invoice", ChildId);

        decision.ContainerId.Should().Be(RootContainer);
        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155: an invoice that is ITSELF secure keeps its own container and never consults its root")]
    public async Task SecurableChild_ItselfSecure_KeepsItsOwnContainer()
    {
        var world = new World(securable: ["sprk_project", "sprk_matter", "sprk_workassignment", "sprk_invoice"])
            .WithChild("sprk_invoice", typedMatter: MatterId, isSecure: true, ownContainer: OtherRootContainer)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_invoice", ChildId);

        decision.ContainerId.Should().Be(OtherRootContainer);
        world.RootReads().Should().Be(0);
    }

    [Fact(DisplayName = "Task 155: a contact (no ancestor concept) resolves its own business-unit container by the two-argument overload")]
    public async Task Contact_TwoArgumentOverload_ResolvesItsBusinessUnitContainer()
    {
        var world = new World()
            .WithRecord("contact", owningBusinessUnit: true)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("contact", ChildId);

        decision.ContainerId.Should().Be(BusinessUnitContainer,
            "the two-argument overload's documented contract: the RECORD's own owningbusinessunit");
    }

    // ============================================================================================
    // The four-argument (explicit fallback) overload — unchanged for records with no secure root
    // ============================================================================================

    [Fact(DisplayName = "Task 155: with an explicit fallback, a child with NO secure root resolves to that fallback, as before")]
    public async Task ExplicitFallback_ChildWithoutASecureRoot_ResolvesTheFallback()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: false, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, ArchiveContainer);

        decision.ContainerId.Should().Be(ArchiveContainer);
        world.Reads("businessunit").Should().Be(0, "an explicit fallback is never second-guessed by the BU");
    }

    [Fact(DisplayName = "Task 155: with an explicit fallback, a child under a SECURE root still goes to the root — the fallback is never in scope")]
    public async Task ExplicitFallback_ChildUnderASecureRoot_ResolvesTheRootNotTheFallback()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, ArchiveContainer);

        decision.ContainerId.Should().Be(RootContainer);
    }

    [Fact(DisplayName = "Task 155: with an explicit fallback, an entity with no ancestor concept still costs ZERO record reads")]
    public async Task ExplicitFallback_EntityWithNoAncestorConcept_CostsNoRecordRead()
    {
        var world = new World();

        var decision = await world.Resolver().ResolveForRecordAsync("contact", ChildId, ArchiveContainer);

        decision.ContainerId.Should().Be(ArchiveContainer);
        world.TotalReads().Should().Be(0);
    }

    // ============================================================================================
    // The refusals — none resolves a shared container
    // ============================================================================================

    [Fact(DisplayName = "Task 155: a SECURE root with no container FAILS CLOSED (secure_record_container_missing) — the BU is never read")]
    public async Task Todo_UnderASecureRootWithoutContainer_FailsClosed()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, containerId: "   ")
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be("secure_record_container_missing");
        ex.StatusCode.Should().Be(409);
        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155: TWO secure roots — ambiguity refusal (container_ancestor_ambiguous)")]
    public async Task Todo_UnderTwoSecureRoots_IsRefusedAsAmbiguous()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId, regardingMatter: MatterId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer)
            .WithRoot("sprk_matter", MatterId, isSecure: true, OtherRootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode);
        ex.StatusCode.Should().Be(409);
    }

    [Fact(DisplayName = "Task 155: an UNREADABLE root refuses (container_ancestor_unresolved, 503) — never read as non-secure")]
    public async Task Todo_WhoseRootCannotBeRead_IsRefused()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRootFault("sprk_project", ProjectId, new TimeoutException("Dataverse timed out"))
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        ex.StatusCode.Should().Be(503, "a transient read failure is retryable, not a verdict");
        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155: a link to a root that does not EXIST refuses (container_ancestor_unresolved, 409)")]
    public async Task Todo_LinkedToARootThatDoesNotExist_IsRefused()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRootFault("sprk_project", ProjectId, new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason("does not exist")))
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        ex.StatusCode.Should().Be(409);
    }

    [Fact(DisplayName = "Task 155: an unreadable CHILD (the link itself) propagates — never read as 'no ancestor'")]
    public async Task Todo_WhoseOwnRowCannotBeRead_Propagates()
    {
        var world = new World().WithRecordFault("sprk_todo", new TimeoutException("Dataverse timed out"));

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, ArchiveContainer);

        await act.Should().ThrowAsync<TimeoutException>(
            "the explicit fallback is available, and using it on an unread link is the #1038 fail-open");
    }

    [Theory(DisplayName = "Task 155 (escalation trigger 2): a child filed under ANOTHER child is refused as unverifiable — its root link is a stamp that can be stale")]
    [InlineData("sprk_todo", "sprk_regardingcommunication")]
    [InlineData("sprk_todo", "sprk_regardingevent")]
    [InlineData("sprk_todo", "sprk_regardinginvoice")]
    [InlineData("sprk_todo", "sprk_regardingdocument")]
    [InlineData("sprk_todo", "sprk_regardinganalysis")]
    [InlineData("sprk_event", "sprk_regardingcommunication")]
    [InlineData("sprk_event", "sprk_regardingevent")]
    public async Task Child_FiledUnderAnotherChild_IsRefusedAsUnverifiable(string entity, string intermediateColumn)
    {
        // The stamp says "non-secure project". Nothing re-stamps this record when its communication is re-filed
        // under a SECURE matter, so trusting the stamp would put a secure root's content in a shared container.
        var world = new World()
            .WithChild(entity, regardingProject: ProjectId, intermediate: (intermediateColumn, CommunicationId))
            .WithRoot("sprk_project", ProjectId, isSecure: false, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync(entity, ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnverifiableCode);
            ex.StatusCode.Should().Be(409);
        }

        world.Reads("businessunit").Should().Be(0);
        world.RootReads().Should().Be(0, "the stamp is not consulted at all — consulting it is the papering-over");
    }

    [Fact(DisplayName = "Task 155 (escalation trigger 1): a CHILD type whose root link is not known is refused without a read")]
    public async Task ChildTypeWithoutKnownLinks_IsRefused()
    {
        var world = new World();

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_document", ChildId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        world.TotalReads().Should().Be(0);
    }

    // ============================================================================================
    // Cost — ONE child read carrying its links, ONE root read
    // ============================================================================================

    [Fact(DisplayName = "Task 155: the child's links ride on ONE record read, and the secure root costs ONE read")]
    public async Task Todo_UnderASecureProject_CostsOneChildReadAndOneRootRead()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer);

        await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        world.Reads("sprk_todo").Should().Be(1);
        world.Reads("sprk_project").Should().Be(1);
        world.TotalReads().Should().Be(2);

        world.ColumnsRead("sprk_todo").Should().Contain(
            CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute),
            "every root link must come back with the child row");
    }

    // ============================================================================================
    // Guard — every child type the upload routes accept has known links
    // ============================================================================================

    [Fact(DisplayName = "Task 155: every CHILD type the record-keyed upload routes accept has known root links (else every upload to it is refused)")]
    public void EveryRouteReachableChildType_HasKnownRootLinks()
    {
        var routeChildTypes = EntityAccessFilter.SupportedEntityTypes
            .Select(DocumentAssociationMap.ToLogicalName)
            .OfType<string>()
            .Where(CoreAncestorResolver.IsChildRecordEntity)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        routeChildTypes.Should().NotBeEmpty("the routes accept to-dos and events");
        routeChildTypes.Should().BeSubsetOf(RecordContainerResolver.ChildAncestorLinks.KnownChildEntities);
    }

    // ============================================================================================
    // MACHINERY — an in-memory org
    // ============================================================================================

    private sealed class World
    {
        private readonly HashSet<string> _securable;
        private readonly Dictionary<(string Entity, Guid Id), Func<Entity>> _rows = new();
        private readonly IGenericEntityService _service = Substitute.For<IGenericEntityService>();

        private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
        {
            "sprk_project", "sprk_matter", "sprk_workassignment", "sprk_servicerequest", "sprk_invoice",
            "sprk_event", "sprk_todo", "sprk_document", "sprk_communication", "contact", "businessunit"
        };

        public World(string[]? securable = null)
        {
            _securable = new HashSet<string>(
                securable ?? ["sprk_project", "sprk_matter", "sprk_workassignment"], StringComparer.Ordinal);

            _service
                .RetrieveAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var key = (call.ArgAt<string>(0), call.ArgAt<Guid>(1));
                    return _rows.TryGetValue(key, out var row)
                        ? Task.FromResult(row())
                        : throw new InvalidOperationException($"Unmodelled read: {key.Item1} {key.Item2}");
                });
        }

        public World WithChild(
            string entity,
            Guid? regardingProject = null,
            Guid? regardingMatter = null,
            Guid? regardingServiceRequest = null,
            Guid? typedMatter = null,
            bool? isSecure = null,
            string? ownContainer = null,
            (string Column, Guid Id)? intermediate = null)
        {
            var row = new Entity(entity, ChildId)
            {
                ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId)
            };

            if (regardingProject is { } p) row["sprk_regardingproject"] = new EntityReference("sprk_project", p);
            if (regardingMatter is { } m) row["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
            if (regardingServiceRequest is { } s)
                row["sprk_regardingservicerequest"] = new EntityReference("sprk_servicerequest", s);
            if (typedMatter is { } tm) row["sprk_matter"] = new EntityReference("sprk_matter", tm);
            if (isSecure is { } secure) row["sprk_issecure"] = secure;
            if (ownContainer is not null) row["sprk_containerid"] = ownContainer;
            if (intermediate is { } i) row[i.Column] = new EntityReference("sprk_communication", i.Id);

            _rows[(entity, ChildId)] = () => row;
            return this;
        }

        public World WithRecord(string entity, bool owningBusinessUnit)
        {
            var row = new Entity(entity, ChildId);
            if (owningBusinessUnit)
            {
                row["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId);
            }

            _rows[(entity, ChildId)] = () => row;
            return this;
        }

        public World WithRecordFault(string entity, Exception fault)
        {
            _rows[(entity, ChildId)] = () => throw fault;
            return this;
        }

        public World WithRoot(string entity, Guid id, bool isSecure, string? containerId)
        {
            var row = new Entity(entity, id) { ["sprk_issecure"] = isSecure };
            if (containerId is not null) row["sprk_containerid"] = containerId;

            _rows[(entity, id)] = () => row;
            return this;
        }

        public World WithRootFault(string entity, Guid id, Exception fault)
        {
            _rows[(entity, id)] = () => throw fault;
            return this;
        }

        public World WithBusinessUnit(string? container)
        {
            var bu = new Entity("businessunit", BusinessUnitId);
            if (container is not null) bu["sprk_containerid"] = container;

            _rows[("businessunit", BusinessUnitId)] = () => bu;
            return this;
        }

        public RecordContainerResolver Resolver()
        {
            var registry = Substitute.For<ISecurableEntityRegistry>();
            registry.ClassifyEntityAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(TestEntityCatalog.Classify(call.Arg<string>(), _securable, Known)));
            registry.GetSecurableEntitiesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlySet<string>>(_securable));

            return new RecordContainerResolver(registry, _service, NullLogger<RecordContainerResolver>.Instance);
        }

        private IEnumerable<ICall> ReadCalls() =>
            _service.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IGenericEntityService.RetrieveAsync));

        public int Reads(string entity) => ReadCalls().Count(c => (string)c.GetArguments()[0]! == entity);

        public int TotalReads() => ReadCalls().Count();

        public int RootReads() => ReadCalls().Count(c =>
            CoreAncestorResolver.IsCoreRecordEntity((string)c.GetArguments()[0]!));

        public IEnumerable<string> ColumnsRead(string entity) => ReadCalls()
            .Where(c => (string)c.GetArguments()[0]! == entity)
            .SelectMany(c => (string[])c.GetArguments()[2]!);
    }
}
