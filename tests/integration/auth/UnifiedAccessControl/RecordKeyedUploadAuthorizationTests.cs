using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using NSubstitute;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 076 — the two halves of the record-keyed upload contract that had no
/// coverage at all before this task:
///
/// <list type="number">
///   <item><description><b>The gate.</b>
///   <c>PUT /api/obo/records/{entityLogicalName}/{recordId}/files/{*path}</c> replaces a route that ran
///   under <c>RequireAuthorization()</c> alone — "are you anyone?" — so there was no per-resource decision
///   to test. These assert that a caller lacking rights on the OWNING RECORD never reaches the handler,
///   and that an entity type whose access cannot be evaluated is DENIED rather than waved through.</description></item>
///   <item><description><b>The record's-own-business-unit fallback.</b>
///   <c>RecordContainerResolver</c>'s two-argument overload derives the non-secure default from the
///   RECORD's <c>owningbusinessunit</c> instead of taking one from the caller. Every pre-existing test in
///   <see cref="RecordContainerResolverTests"/> calls the THREE-argument overload and passes a fallback
///   explicitly, so the derivation itself — and, more importantly, the fact that it is SKIPPED for a
///   secure record — was entirely unpinned.</description></item>
/// </list>
///
/// <para><b>Why the deny tests are the ones that matter, and why they were perturbation-checked.</b> This
/// project has already been burned by a suite that stayed green against a broken read: 45 dedicated tests
/// passed while the thing they nominally covered was inert. A deny test that passes when the gate is
/// removed is worse than no test, because it certifies the hole. Each assertion below was verified to go
/// RED with the gate broken — see the task notes for the transcript.</para>
/// </summary>
public class RecordKeyedUploadAuthorizationTests
{
    private const string MappedEntity = "sprk_matter";
    private const string MappedEntitySet = "sprk_matters";
    // This constant has now moved TWICE, each time because the type it named got mapped:
    //   2026-09-03  sprk_workassignment -> sprk_todo   (Q4 widening mapped work assignment)
    //   2026-09-04  sprk_todo           -> account     (sprk_relatedtodo turned out to exist)
    //
    // The 2026-09-03 move called `sprk_todo` "genuinely unmappable rather than merely not-yet-mapped".
    // That was wrong — `sprk_relatedtodo` existed the whole time; the claim came from searching only
    // the bare `sprk_{type}` family. So the deny path was once again not being tested, which is the
    // exact defect that move was made to fix.
    //
    // `account` is the first value here that is unmappable for a REASON rather than by omission:
    // `sprk_document` has no account lookup in EITHER column family, and the owner decided on
    // 2026-09-04 that it never will (Spaarke's organization analogue is `sprk_organization`). It was
    // removed from BOTH EntityAccessFilter.EntitySetByType and the Office save allow-list on that
    // basis, so it is unmappable by policy, not by accident.
    //
    // If account is ever mapped, move this again rather than deleting the test — and note that the
    // recurrence itself is the signal: pick the value from what the maps EXCLUDE ON PURPOSE, never
    // from what merely happens to be missing today.
    private const string UnmappedEntity = "account";
    private const string OwnContainer = "b!secure-own-container-0000000000";
    private const string BusinessUnitContainer = "b!record-bu-container-00000000000";

    private static readonly Guid RecordId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BusinessUnitId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // The right the route demands. Reused from OperationAccessPolicy rather than restated as a literal,
    // so a change to what "attach content to a record" costs cannot leave these tests asserting the old
    // price while claiming to assert the current one.
    private static readonly AccessRights RequiredRights =
        OperationAccessPolicy.GetRequiredRights(RecordRouteAccessAuthorizationFilter.AssociateContentOperation);

    // ============================================================================================
    // THE GATE — a caller without access to the owning record must never reach the handler
    // ============================================================================================

    [Fact(DisplayName = "Task 076: a caller with NO rights on the owning record is DENIED and the handler never runs")]
    public async Task Upload_WhenCallerHasNoRightsOnTheOwningRecord_IsDeniedAndHandlerNeverRuns()
    {
        var probe = new StubProbe(AccessRights.None);
        var handlerRan = false;

        var result = await Invoke(probe, MappedEntity, RecordId, () => handlerRan = true);

        handlerRan.Should().BeFalse(
            "the decision runs in an endpoint filter (ADR-008), so no container is resolved and no bytes "
            + "reach Graph for a caller who cannot append to the record");
        await AssertForbidden(result, "insufficient_rights");

        probe.Calls.Should().Be(1);
        probe.LastEntitySet.Should().Be(MappedEntitySet, "the probe needs the PLURAL collection name");
        probe.LastRecordId.Should().Be(RecordId);
    }

    [Fact(DisplayName = "Task 076: Read alone is not enough to upload against a record")]
    public async Task Upload_WhenCallerHoldsOnlyRead_IsDenied()
    {
        // The interesting near-miss: a caller who can SEE the matter but not append to it. Asserting the
        // boundary rather than only the empty case is what stops a future `rights != None` shortcut from
        // passing.
        var probe = new StubProbe(AccessRights.Read);
        var handlerRan = false;

        var result = await Invoke(probe, MappedEntity, RecordId, () => handlerRan = true);

        handlerRan.Should().BeFalse();
        await AssertForbidden(result, "insufficient_rights");
    }

    [Fact(DisplayName = "Task 076: a caller holding the required rights reaches the handler")]
    public async Task Upload_WhenCallerHoldsRequiredRights_ReachesTheHandler()
    {
        // The positive control. A gate that denies everything would make every test above pass while
        // breaking 100% of uploads — which is precisely the failure the OBO upload waiver warned about
        // (attaching DocumentAuthorizationFilter would have denied every caller).
        var probe = new StubProbe(RequiredRights);
        var handlerRan = false;

        var result = await Invoke(probe, MappedEntity, RecordId, () => handlerRan = true);

        handlerRan.Should().BeTrue();
        result.Should().Be("handler-ran");
    }

    [Fact(DisplayName = "Task 076: an entity logical name outside the shared map DENIES rather than passing through")]
    public async Task Upload_WhenEntityTypeIsNotAuthorizable_IsDeniedWithoutProbing()
    {
        // UnmappedEntity is NOT in EntityAccessFilter's logical-name -> entity-set table. It must deny,
        // not proceed: an entity whose per-record access nothing here can evaluate is an entity whose
        // uploads cannot be accepted, because accepting one writes bytes into a container on the
        // strength of no decision.
        var probe = new StubProbe(RequiredRights);
        var handlerRan = false;

        var result = await Invoke(probe, UnmappedEntity, RecordId, () => handlerRan = true);

        handlerRan.Should().BeFalse();
        await AssertForbidden(result, "entity_type_not_authorizable");

        probe.Calls.Should().Be(0,
            "there is no entity set to ask Dataverse about, so the denial must precede the probe");
    }

    [Fact(DisplayName = "Task 076: a route with no usable owning-record key DENIES rather than proceeding")]
    public async Task Upload_WhenRouteCarriesNoOwningRecord_IsDenied()
    {
        // Unreachable through the mapped routes (both segments are required and {recordId:guid} is
        // constrained), so this pins the filter's behaviour if it is ever attached to a route that does not
        // carry the key. EntityAccessFilter deliberately calls next() when it finds no target — correct for
        // the Office save path, catastrophic here — so the divergence is asserted rather than assumed.
        var probe = new StubProbe(RequiredRights);
        var handlerRan = false;

        var result = await Invoke(probe, MappedEntity, Guid.Empty, () => handlerRan = true);

        handlerRan.Should().BeFalse();
        await AssertForbidden(result, "owning_record_not_specified");
        probe.Calls.Should().Be(0);
    }

    [Fact(DisplayName = "Task 076: a probe failure DENIES — an unanswerable access question is not an allowed one")]
    public async Task Upload_WhenTheProbeThrows_IsDenied()
    {
        var probe = new StubProbe(AccessRights.None, throws: true);
        var handlerRan = false;

        var result = await Invoke(probe, MappedEntity, RecordId, () => handlerRan = true);

        handlerRan.Should().BeFalse();
        await AssertForbidden(result, "access_check_failed");
    }

    // ============================================================================================
    // THE RECORD'S-OWN-BUSINESS-UNIT FALLBACK — the two-argument overload
    // ============================================================================================

    [Fact(DisplayName = "Task 076: a NON-secure record resolves through the RECORD's own owningbusinessunit")]
    public async Task TwoArgOverload_NonSecureRecord_ResolvesThroughTheRecordsOwnBusinessUnit()
    {
        var entityService = Substitute.For<IGenericEntityService>();
        StubRecordRead(entityService, isSecure: false, ownContainerId: null, withOwningBusinessUnit: true);
        StubBusinessUnitRead(entityService, BusinessUnitContainer);

        var resolver = BuildResolver(entityService);

        // TWO arguments. No caller-supplied fallback exists to fall back TO, so a pass here can only come
        // from the server deriving it from the record itself.
        var decision = await resolver.ResolveForRecordAsync(MappedEntity, RecordId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedFallback);
        decision.ContainerId.Should().Be(BusinessUnitContainer);

        await entityService.Received(1).RetrieveAsync(
            "businessunit", BusinessUnitId, Arg.Any<string[]>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Task 076: a SECURE record with no container FAILS CLOSED and its business unit is never read")]
    public async Task TwoArgOverload_SecureRecordWithoutContainer_FailsClosed_AndNeverReadsTheBusinessUnit()
    {
        // THE LOAD-BEARING ONE. The business-unit lookup is skipped for a secure record deliberately, so
        // that the fail-closed path cannot acquire a usable fallback in the first place. Asserting only the
        // throw would pass even if the BU container were fetched and then discarded — one refactor away
        // from being used. So the absence of the read is asserted too.
        var entityService = Substitute.For<IGenericEntityService>();
        StubRecordRead(entityService, isSecure: true, ownContainerId: null, withOwningBusinessUnit: true);
        StubBusinessUnitRead(entityService, BusinessUnitContainer);

        var resolver = BuildResolver(entityService);

        var act = async () => await resolver.ResolveForRecordAsync(MappedEntity, RecordId);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("secure_record_container_missing");

        await entityService.DidNotReceive().RetrieveAsync(
            "businessunit", Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Task 076: a SECURE record resolves to its OWN container without consulting its business unit")]
    public async Task TwoArgOverload_SecureRecord_ResolvesToItsOwnContainer()
    {
        var entityService = Substitute.For<IGenericEntityService>();
        StubRecordRead(entityService, isSecure: true, ownContainerId: OwnContainer, withOwningBusinessUnit: true);
        StubBusinessUnitRead(entityService, BusinessUnitContainer);

        var resolver = BuildResolver(entityService);

        var decision = await resolver.ResolveForRecordAsync(MappedEntity, RecordId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedSecure);
        decision.ContainerId.Should().Be(OwnContainer,
            "the record's own container wins; the business-unit container must not be substituted");

        await entityService.DidNotReceive().RetrieveAsync(
            "businessunit", Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Task 076: a business unit with no container leaves a non-secure record Unresolved, not failed")]
    public async Task TwoArgOverload_BusinessUnitWithoutContainer_YieldsUnresolved()
    {
        // A legitimate and common state — three of six business units had sprk_containerid unset when this
        // was measured. It must read as "no container available" (which the upload route turns into a 409
        // the operator can act on), never as a secure-record refusal.
        var entityService = Substitute.For<IGenericEntityService>();
        StubRecordRead(entityService, isSecure: false, ownContainerId: null, withOwningBusinessUnit: true);
        StubBusinessUnitRead(entityService, container: null);

        var resolver = BuildResolver(entityService);

        var decision = await resolver.ResolveForRecordAsync(MappedEntity, RecordId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.Unresolved);
        decision.ContainerId.Should().BeNull();
    }

    [Fact(DisplayName = "Task 076: an organization-owned record (no owning business unit) is Unresolved, not failed")]
    public async Task TwoArgOverload_RecordWithNoOwningBusinessUnit_YieldsUnresolved()
    {
        var entityService = Substitute.For<IGenericEntityService>();
        StubRecordRead(entityService, isSecure: false, ownContainerId: null, withOwningBusinessUnit: false);

        var resolver = BuildResolver(entityService);

        var decision = await resolver.ResolveForRecordAsync(MappedEntity, RecordId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.Unresolved);

        await entityService.DidNotReceive().RetrieveAsync(
            "businessunit", Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>());
    }

    // ============================================================================================
    // TASK 151 (#1038) — an ALIAS in the route authorizes and resolves the SAME record
    // ============================================================================================

    [Theory(DisplayName = "Task 151: an alias route value authorizes AND resolves the same record — no misleading 'no container' 409")]
    [InlineData("project", "sprk_project", "sprk_projects")]
    [InlineData("matter", "sprk_matter", "sprk_matters")]
    public async Task AliasRouteValue_AuthorizesAndResolvesTheSameRecord_NonSecure(
        string routeValue, string logicalName, string entitySet)
    {
        // The route hands ONE string to both halves: the filter (authorization key) and the handler's resolver
        // call (container key). Before task 151 the filter mapped the alias and the resolver did not — it read
        // "project" as not-securable, derived no container, and the handler answered "No storage container is
        // configured" (409) for a record whose business-unit container was derivable.
        var probe = new StubProbe(RequiredRights);
        var handlerRan = false;

        var gate = await Invoke(probe, routeValue, RecordId, () => handlerRan = true);

        handlerRan.Should().BeTrue();
        gate.Should().Be("handler-ran");
        probe.LastEntitySet.Should().Be(entitySet);
        probe.LastRecordId.Should().Be(RecordId);

        var entityService = Substitute.For<IGenericEntityService>();
        StubRecordRead(entityService, isSecure: false, ownContainerId: null, withOwningBusinessUnit: true, entity: logicalName);
        StubBusinessUnitRead(entityService, BusinessUnitContainer);

        var decision = await BuildResolver(entityService).ResolveForRecordAsync(routeValue, RecordId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedFallback,
            "Unresolved here is exactly the misleading 409 the handler returns");
        decision.ContainerId.Should().Be(BusinessUnitContainer);

        // The container came from the record the filter authorized: same logical entity, same id.
        await entityService.Received(1).RetrieveAsync(
            logicalName, RecordId, Arg.Any<string[]>(), Arg.Any<CancellationToken>());
    }

    [Theory(DisplayName = "Task 151: an alias route value for a SECURE record resolves to its OWN container")]
    [InlineData("project", "sprk_project")]
    [InlineData("matter", "sprk_matter")]
    public async Task AliasRouteValue_SecureRecord_ResolvesItsOwnContainer(string routeValue, string logicalName)
    {
        var entityService = Substitute.For<IGenericEntityService>();
        StubRecordRead(entityService, isSecure: true, ownContainerId: OwnContainer, withOwningBusinessUnit: true, entity: logicalName);
        StubBusinessUnitRead(entityService, BusinessUnitContainer);

        var decision = await BuildResolver(entityService).ResolveForRecordAsync(routeValue, RecordId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedSecure);
        decision.ContainerId.Should().Be(OwnContainer);
    }

    [Fact(DisplayName = "Task 151: an unknown route entity is DENIED by the filter, and the resolver REFUSES it rather than answering 'no container'")]
    public async Task UnknownRouteEntity_IsDeniedByTheFilter_AndRefusedByTheResolver()
    {
        const string unknown = "sprk_projectt";

        var probe = new StubProbe(RequiredRights);
        var handlerRan = false;

        var gate = await Invoke(probe, unknown, RecordId, () => handlerRan = true);

        handlerRan.Should().BeFalse("an unknown entity is never uploaded");
        await AssertForbidden(gate, "entity_type_not_authorizable");

        // Defence in depth: should the name ever reach the resolver, it is a typed refusal — NOT the
        // Unresolved outcome the handler renders as "No storage container is configured".
        var entityService = Substitute.For<IGenericEntityService>();
        var act = async () => await BuildResolver(entityService).ResolveForRecordAsync(unknown, RecordId);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.Code.Should().Be("container_entity_unknown");
        await entityService.DidNotReceiveWithAnyArgs().RetrieveAsync(default!, default, default!, default);
    }

    // ============================================================================================
    // MACHINERY
    // ============================================================================================

    /// <summary>
    /// Run the filter over a synthetic request and return either the filter's short-circuit result or the
    /// sentinel the inner handler produces when it is reached.
    /// </summary>
    private static async Task<object?> Invoke(
        CallerRecordAccessProbe probe,
        string entityLogicalName,
        Guid recordId,
        Action onHandlerRun)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer caller-token";
        httpContext.Request.RouteValues[RecordRouteAccessAuthorizationFilter.EntityLogicalNameRouteValue] =
            entityLogicalName;
        httpContext.Request.RouteValues[RecordRouteAccessAuthorizationFilter.RecordIdRouteValue] =
            recordId == Guid.Empty ? null : recordId.ToString();

        var filter = new RecordRouteAccessAuthorizationFilter(
            probe,
            RecordRouteAccessAuthorizationFilter.AssociateContentOperation,
            NullLogger<RecordRouteAccessAuthorizationFilter>.Instance);

        var context = EndpointFilterInvocationContext.Create(httpContext);

        return await filter.InvokeAsync(context, _ =>
        {
            onHandlerRun();
            return ValueTask.FromResult<object?>("handler-ran");
        });
    }

    /// <summary>
    /// Assert the filter short-circuited with a 403 carrying the expected <c>reasonCode</c>. The reason code
    /// is part of the contract — the client distinguishes "you may not" from "we could not tell" by it — so
    /// asserting only the status would let the codes drift silently.
    /// </summary>
    /// <remarks>
    /// Inspects the TYPED result rather than executing it. <c>ProblemHttpResult.ExecuteAsync</c> resolves
    /// <c>IProblemDetailsService</c> off <c>HttpContext.RequestServices</c>, so rendering it would require
    /// standing up a service provider purely to read a status code the object already carries — and a
    /// helper that throws on missing DI reports a wiring problem as a gate failure, which is exactly the
    /// kind of misdirection these tests exist to avoid.
    /// </remarks>
    private static Task AssertForbidden(object? result, string expectedReasonCode)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;

        problem.StatusCode.Should().Be(403);
        problem.ProblemDetails.Extensions.Should().ContainKey("reasonCode");
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(expectedReasonCode);

        return Task.CompletedTask;
    }

    private static RecordContainerResolver BuildResolver(IGenericEntityService entityService)
    {
        var registry = Substitute.For<ISecurableEntityRegistry>();
        var securable = new HashSet<string>(StringComparer.Ordinal) { MappedEntity, "sprk_project" };

        // Task 151: the org's entities by LOGICAL name only, as the real registry knows them — so an alias that
        // the resolver failed to map would be refused here, not silently answered.
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            MappedEntity, "sprk_project", "sprk_workassignment", "sprk_invoice", "sprk_event", "sprk_todo",
            "contact", UnmappedEntity
        };

        registry.GetSecurableEntitiesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(securable));
        registry.ClassifyEntityAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(TestEntityCatalog.Classify(call.Arg<string>(), securable, known)));

        return new RecordContainerResolver(
            registry, entityService, NullLogger<RecordContainerResolver>.Instance);
    }

    private static void StubRecordRead(
        IGenericEntityService entityService,
        bool isSecure,
        string? ownContainerId,
        bool withOwningBusinessUnit,
        string entity = MappedEntity)
    {
        var row = new Entity(entity, RecordId) { ["sprk_issecure"] = isSecure };

        if (ownContainerId is not null)
        {
            row["sprk_containerid"] = ownContainerId;
        }

        if (withOwningBusinessUnit)
        {
            row["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId);
        }

        entityService
            .RetrieveAsync(entity, RecordId, Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(row));
    }

    private static void StubBusinessUnitRead(IGenericEntityService entityService, string? container)
    {
        var bu = new Entity("businessunit", BusinessUnitId);

        if (container is not null)
        {
            bu["sprk_containerid"] = container;
        }

        entityService
            .RetrieveAsync("businessunit", BusinessUnitId, Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(bu));
    }

    /// <summary>
    /// In-memory double for the ONE virtual member <see cref="CallerRecordAccessProbe"/> designates as its
    /// substitution seam (ADR-010 keeps the type concrete; ADR-038 §4 blesses the virtual-member boundary).
    /// Hand-rolled rather than mocked so the recorded arguments read at the assertion site — the entity SET
    /// it was asked about is a real part of the contract, since the probe needs the plural collection and a
    /// singular name would silently answer None for every caller.
    /// </summary>
    private sealed class StubProbe : CallerRecordAccessProbe
    {
        private readonly AccessRights _rights;
        private readonly bool _throws;

        public int Calls { get; private set; }
        public string? LastEntitySet { get; private set; }
        public Guid LastRecordId { get; private set; }

        public StubProbe(AccessRights rights, bool throws = false)
            : base(
                new HttpClient(),
                new ConfigurationBuilder().Build(),
                NullLogger<CallerRecordAccessProbe>.Instance)
        {
            _rights = rights;
            _throws = throws;
        }

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken,
            string entitySet,
            Guid recordId,
            CancellationToken ct = default)
        {
            Calls++;
            LastEntitySet = entitySet;
            LastRecordId = recordId;

            if (_throws)
            {
                throw new InvalidOperationException("Dataverse unreachable");
            }

            return Task.FromResult(_rights);
        }
    }
}

/// <summary>
/// unified-access-control-r2 task 155 (#1080) — the REAL record-keyed upload route, end to end: route →
/// <see cref="RecordRouteAccessAuthorizationFilter"/> → handler → <see cref="RecordContainerResolver"/> → the drive the
/// bytes reach. The task 151 verifier's test-shape gap was that the filter and the resolver had only ever been
/// composed BY HAND in a test; nothing proved the mapped route wires them together. Only module boundaries are
/// substituted: the caller-rights probe, Dataverse rows, the securable-entity registry, and the SPE facade's upload
/// (which records the drive it was handed — the load-bearing assertion).
/// </summary>
public class RecordKeyedUploadRouteChildRecordTests : IClassFixture<RecordKeyedUploadRouteFixture>
{
    private readonly RecordKeyedUploadRouteFixture _fixture;

    public RecordKeyedUploadRouteChildRecordTests(RecordKeyedUploadRouteFixture fixture)
    {
        _fixture = fixture;
        _fixture.Uploads.Clear();
        _fixture.RestampQueue.Children.Clear();
    }

    [Fact(DisplayName = "Task 155: PUT /api/obo/records/sprk_todo/{id}/files/… for a to-do under a SECURE project stores the file in the PROJECT's own container")]
    public async Task Put_TodoUnderASecureProject_StoresInTheProjectsOwnContainer()
    {
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/sprk_todo/{RecordKeyedUploadRouteFixture.TodoUnderSecureProject}/files/brief.docx",
            new ByteArrayContent([1, 2, 3]));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().ContainSingle()
            .Which.Should().Be(RecordKeyedUploadRouteFixture.SecureProjectContainer,
                "a child of a secure record is secure — its bytes belong in the secure project's own container");
    }

    [Fact(DisplayName = "Task 171: POST upload-session for a to-do under a SECURE project opens an APP-ONLY session on the PROJECT's own container (the 2026-10-06 upload403 path)")]
    public async Task UploadSession_TodoUnderASecureProject_OpensAnAppOnlySessionOnTheProjectsOwnContainer()
    {
        var response = await _fixture.Client().PostAsync(
            $"/api/obo/records/sprk_todo/{RecordKeyedUploadRouteFixture.TodoUnderSecureProject}/upload-session?path=big.pdf",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().ContainSingle()
            .Which.Should().Be("session:" + RecordKeyedUploadRouteFixture.SecureProjectContainer,
                "the secure container has no members by design, so only an app-only session can be created on it — and "
                + "the container is the one derived from the authorized record, never the caller's");
    }

    [Fact(DisplayName = "Task 171: POST upload-session for a to-do under an UNPROVISIONED secure project is refused and opens NO session")]
    public async Task UploadSession_TodoUnderAnUnprovisionedSecureProject_OpensNoSession()
    {
        var response = await _fixture.Client().PostAsync(
            $"/api/obo/records/sprk_todo/{RecordKeyedUploadRouteFixture.TodoUnderUnprovisionedSecureProject}/upload-session?path=big.pdf",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().BeEmpty("an app-only session is opened only on a container derived from the authorized record");
    }

    [Fact(DisplayName = "Task 155: the same route for a to-do under a NON-secure project stores the file in the to-do's OWN business-unit container — no 409")]
    public async Task Put_TodoUnderANonSecureProject_StoresInItsBusinessUnitContainer()
    {
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/todo/{RecordKeyedUploadRouteFixture.TodoUnderPlainProject}/files/notes.txt",
            new ByteArrayContent([4, 5, 6]));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().ContainSingle().Which.Should().Be(RecordKeyedUploadRouteFixture.BusinessUnitContainer);
    }

    [Fact(DisplayName = "Task 156: a to-do whose copy of its communication's root is STALE (the communication now regards the SECURE project) is refused 409 container_ancestor_stale, NOTHING reaches SPE, and the to-do is enqueued for re-stamping")]
    public async Task Put_TodoFiledUnderACommunication_WithAStaleCopy_IsRefused_WritesNothing_AndIsEnqueued()
    {
        // Task 155 refused every to-do under a communication (unverifiable). Task 156 reads the communication live: its
        // root is the SECURE project, the to-do's copy still says the plain one — trusting it was the #1038 leak.
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/sprk_todo/{RecordKeyedUploadRouteFixture.TodoUnderCommunication}/files/notes.txt",
            new ByteArrayContent([7]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain(RecordContainerResolver.AncestorStaleCode);
        _fixture.Uploads.Should().BeEmpty("a refusal is only a refusal if no bytes moved");
        _fixture.RestampQueue.Children.Should().ContainSingle()
            .Which.Should().Be(("sprk_todo", RecordKeyedUploadRouteFixture.TodoUnderCommunication));
    }

    [Fact(DisplayName = "Task 156: a to-do whose copy EQUALS its communication's live root (the SECURE project) stores the file in the PROJECT's own container")]
    public async Task Put_TodoFiledUnderACommunication_WithAFreshCopy_StoresInTheProjectsOwnContainer()
    {
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/todo/{RecordKeyedUploadRouteFixture.TodoUnderCommunicationFreshCopy}/files/fresh.docx",
            new ByteArrayContent([15]));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().ContainSingle()
            .Which.Should().Be(RecordKeyedUploadRouteFixture.SecureProjectContainer,
                "resolved exactly as a direct link to the secure project would be");
        _fixture.RestampQueue.Children.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 156 (verifier round 1 item 7): a to-do whose copy EQUALS its communication's live root, a NON-secure project, stores the file in the to-do's OWN business-unit container — the non-secure half of AC4, through the real PUT route")]
    public async Task Put_TodoFiledUnderACommunication_WithAFreshCopy_UnderANonSecureProject_StoresInItsBusinessUnitContainer()
    {
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/sprk_todo/{RecordKeyedUploadRouteFixture.TodoUnderPlainCommunicationFreshCopy}/files/plain.docx",
            new ByteArrayContent([16]));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().ContainSingle()
            .Which.Should().Be(RecordKeyedUploadRouteFixture.BusinessUnitContainer,
                "resolved exactly as a direct link to the non-secure project would be: the record's business-unit container "
                + "(task 155 refused this shape with container_ancestor_unverifiable)");
        _fixture.RestampQueue.Children.Should().BeEmpty("a fresh copy is not stale");
    }

    [Fact(DisplayName = "Task 156 (verifier round 2, V2): PUT for a to-do under a communication whose SECURE project is named ONLY by the communication's polymorphic pair stores the file in the PROJECT's own container — never the to-do's business unit")]
    public async Task Put_TodoUnderACommunicationWhoseSecureProjectIsNamedOnlyByItsPair_StoresInTheProjectsOwnContainer()
    {
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/sprk_todo/{RecordKeyedUploadRouteFixture.TodoUnderCommunicationPairedToSecureProject}/files/pair.docx",
            new ByteArrayContent([17]));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().ContainSingle()
            .Which.Should().Be(RecordKeyedUploadRouteFixture.SecureProjectContainer,
                "the communication's pair names the secure project; the to-do's business-unit container is the #1038 leak");
        _fixture.RestampQueue.Children.Should().BeEmpty("the copy is fresh: neither side names a typed root");
    }

    [Fact(DisplayName = "Task 156 (verifier round 2, V3): PUT for a to-do under an analysis filed under the PLAIN project, whose input document belongs to the SECURE project, is refused 409 container_ancestor_ambiguous and NOTHING reaches SPE")]
    public async Task Put_TodoUnderAnAnalysisWhoseInputDocumentIsUnderTheSecureProject_IsRefusedAsAmbiguous_AndWritesNothing()
    {
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/sprk_todo/{RecordKeyedUploadRouteFixture.TodoUnderAnalysisOfASecureDocument}/files/a.docx",
            new ByteArrayContent([18]));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().Contain(RecordContainerResolver.AncestorAmbiguousCode);
        _fixture.Uploads.Should().BeEmpty("without the carrier read this resolved the to-do's business-unit container");
        _fixture.RestampQueue.Children.Should().BeEmpty("a disagreement is not a stale copy");
    }

    [Fact(DisplayName = "Task 156 (verifier round 2, V1): PUT for a to-do filed under a communication (copy fresh, the PLAIN project) that ALSO names an event of the SECURE project is refused 409 container_ancestor_ambiguous and NOTHING reaches SPE")]
    public async Task Put_TodoUnderACommunicationAlsoNamingAnEventOfTheSecureProject_IsRefusedAsAmbiguous_AndWritesNothing()
    {
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/sprk_todo/{RecordKeyedUploadRouteFixture.TodoUnderCommunicationCarryingSecureEvent}/files/e.docx",
            new ByteArrayContent([19]));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().Contain(RecordContainerResolver.AncestorAmbiguousCode);
        _fixture.Uploads.Should().BeEmpty("without the carrier read this resolved the to-do's business-unit container");
        _fixture.RestampQueue.Children.Should().BeEmpty("a disagreement is not a stale copy");
    }

    [Fact(DisplayName = "Task 155: a to-do under a SECURE project with NO container is refused (409 secure_record_container_missing) and NOTHING reaches SPE")]
    public async Task Put_TodoUnderASecureProjectWithoutContainer_FailsClosed_AndWritesNothing()
    {
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/sprk_todo/{RecordKeyedUploadRouteFixture.TodoUnderUnprovisionedSecureProject}/files/x.txt",
            new ByteArrayContent([8]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("secure_record_container_missing");
        _fixture.Uploads.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 155: a to-do whose own row (its ancestor LINK) cannot be read answers 503 container_ancestor_unresolved — not a generic 500 — and NOTHING reaches SPE")]
    public async Task Put_TodoWhoseRowCannotBeRead_Answers503WithAReasonCode_AndWritesNothing()
    {
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/sprk_todo/{RecordKeyedUploadRouteFixture.TodoWhoseRowCannotBeRead}/files/x.txt",
            new ByteArrayContent([9]));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
        body.Should().Contain(RecordContainerResolver.AncestorUnresolvedCode);
        body.Should().NotContain("Upload failed", "the route's catch-all 500 is for faults, not for a typed refusal");
        body.Should().NotContain(RecordKeyedUploadRouteFixture.UnreadableRowFaultText,
            "the raw fault text belongs in the log, not the response");
        _fixture.Uploads.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 155 f3: PUT for a to-do linked to a SECURE project ONLY through the polymorphic regarding pair stores the file in the PROJECT's own container")]
    public async Task Put_TodoLinkedOnlyByThePolymorphicPair_ToASecureProject_StoresInTheProjectsOwnContainer()
    {
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/todo/{RecordKeyedUploadRouteFixture.TodoLinkedOnlyByPairToSecureProject}/files/pair.docx",
            new ByteArrayContent([11]));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().ContainSingle()
            .Which.Should().Be(RecordKeyedUploadRouteFixture.SecureProjectContainer,
                "a row whose only link is the polymorphic pair is still that project's child");
    }

    [Fact(DisplayName = "Task 155 f3: PUT for an invoice regarding an AGREEMENT is refused (409 container_ancestor_unverifiable) and NOTHING reaches SPE")]
    public async Task Put_InvoiceRegardingAnAgreement_IsRefused_AndWritesNothing()
    {
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/invoice/{RecordKeyedUploadRouteFixture.InvoiceRegardingAgreement}/files/inv.pdf",
            new ByteArrayContent([12]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain(RecordContainerResolver.AncestorUnverifiableCode);
        _fixture.Uploads.Should().BeEmpty("before f3 this resolved the shared business-unit container");
    }

    [Fact(DisplayName = "Task 155 f4: PUT for an event under a NON-secure work assignment that is itself filed under a SECURE project stores the file in the PROJECT's own container — the two-hop fail-open, through the real route")]
    public async Task Put_EventUnderANonSecureWorkAssignment_UnderASecureProject_StoresInTheProjectsOwnContainer()
    {
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/event/{RecordKeyedUploadRouteFixture.EventUnderWorkAssignmentUnderSecureProject}/files/w.docx",
            new ByteArrayContent([13]));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().ContainSingle()
            .Which.Should().Be(RecordKeyedUploadRouteFixture.SecureProjectContainer,
                "before f4 the event read only the work assignment's own flag and took the shared BU container");
    }

    [Fact(DisplayName = "Task 155 f4: PUT for an event under a work assignment whose pair names a project that NO LONGER EXISTS is refused (409 container_ancestor_unresolved) and NOTHING reaches SPE — the live a30254d0 shape")]
    public async Task Put_EventUnderAWorkAssignmentWithADanglingPair_IsRefused_AndWritesNothing()
    {
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/sprk_event/{RecordKeyedUploadRouteFixture.EventUnderWorkAssignmentWithDanglingPair}/files/x.txt",
            new ByteArrayContent([14]));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().Contain(RecordContainerResolver.AncestorUnresolvedCode);
        _fixture.Uploads.Should().BeEmpty("before f4 this resolved the shared business-unit container");
    }

    [Fact(DisplayName = "Task 155: an event with no ancestor whose business unit has NO container answers the documented 409 with precise copy, and NOTHING reaches SPE")]
    public async Task Put_EventWithNoAncestor_AndABusinessUnitWithoutContainer_AnswersTheDocumented409()
    {
        var response = await _fixture.Client().PutAsync(
            $"/api/obo/records/event/{RecordKeyedUploadRouteFixture.EventInBusinessUnitWithoutContainer}/files/x.txt",
            new ByteArrayContent([10]));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().Contain("No storage container is configured");
        body.Should().Contain("No SharePoint Embedded container could be derived for this record's business unit");
        _fixture.Uploads.Should().BeEmpty();
    }
}

/// <summary>Test host for <see cref="RecordKeyedUploadRouteChildRecordTests"/>. See that class.</summary>
public sealed class RecordKeyedUploadRouteFixture : CustomWebAppFactory
{
    public const string SecureProjectContainer = "b!secure-project-own-container-000";
    public const string BusinessUnitContainer = "b!todo-business-unit-container-000";

    public static readonly Guid TodoUnderSecureProject = Guid.Parse("15500000-0000-0000-0000-000000000001");
    public static readonly Guid TodoUnderPlainProject = Guid.Parse("15500000-0000-0000-0000-000000000002");
    public static readonly Guid TodoUnderCommunication = Guid.Parse("15500000-0000-0000-0000-000000000003");
    public static readonly Guid TodoUnderUnprovisionedSecureProject = Guid.Parse("15500000-0000-0000-0000-000000000004");
    public static readonly Guid TodoWhoseRowCannotBeRead = Guid.Parse("15500000-0000-0000-0000-000000000005");
    public static readonly Guid EventInBusinessUnitWithoutContainer = Guid.Parse("15500000-0000-0000-0000-000000000006");
    public static readonly Guid TodoLinkedOnlyByPairToSecureProject = Guid.Parse("15500000-0000-0000-0000-000000000007");
    public static readonly Guid InvoiceRegardingAgreement = Guid.Parse("15500000-0000-0000-0000-000000000008");

    public static readonly Guid EventUnderWorkAssignmentUnderSecureProject = Guid.Parse("15500000-0000-0000-0000-000000000009");
    public static readonly Guid EventUnderWorkAssignmentWithDanglingPair = Guid.Parse("15500000-0000-0000-0000-000000000011");
    public static readonly Guid TodoUnderCommunicationFreshCopy = Guid.Parse("15600000-0000-0000-0000-000000000015");
    public static readonly Guid TodoUnderPlainCommunicationFreshCopy = Guid.Parse("15600000-0000-0000-0000-000000000016");
    private static readonly Guid CommunicationUnderPlainProject = Guid.Parse("15600000-0000-0000-0000-000000000017");

    // Task 156 verifier round 2 (V1-V3): the three Source-branch guards no test pinned.
    public static readonly Guid TodoUnderCommunicationPairedToSecureProject = Guid.Parse("15600000-0000-0000-0000-000000000020");
    private static readonly Guid CommunicationPairedToSecureProject = Guid.Parse("15600000-0000-0000-0000-000000000021");
    public static readonly Guid TodoUnderAnalysisOfASecureDocument = Guid.Parse("15600000-0000-0000-0000-000000000022");
    private static readonly Guid AnalysisUnderPlainProject = Guid.Parse("15600000-0000-0000-0000-000000000023");
    private static readonly Guid DocumentUnderSecureProject = Guid.Parse("15600000-0000-0000-0000-000000000024");
    public static readonly Guid TodoUnderCommunicationCarryingSecureEvent = Guid.Parse("15600000-0000-0000-0000-000000000025");
    private static readonly Guid EventUnderSecureProject = Guid.Parse("15600000-0000-0000-0000-000000000026");

    private static readonly Guid ProjectTypeRef = Guid.Parse("ca68b3bb-8600-f111-8407-7c1e520aa4df");
    private static readonly Guid Agreement = Guid.Parse("15500000-0000-0000-0000-000000000010");
    private static readonly Guid WorkAssignmentUnderSecureProject = Guid.Parse("15500000-0000-0000-0000-000000000012");
    private static readonly Guid WorkAssignmentWithDanglingPair = Guid.Parse("15500000-0000-0000-0000-000000000013");
    private static readonly Guid DeletedProject = Guid.Parse("15500000-0000-0000-0000-000000000014");

    public const string UnreadableRowFaultText = "Dataverse timed out reading the to-do";

    private static readonly Guid BusinessUnitWithoutContainer = Guid.Parse("15500000-0000-0000-0000-00000000000f");

    private static readonly Guid SecureProject = Guid.Parse("15500000-0000-0000-0000-00000000000a");
    private static readonly Guid PlainProject = Guid.Parse("15500000-0000-0000-0000-00000000000b");
    private static readonly Guid UnprovisionedSecureProject = Guid.Parse("15500000-0000-0000-0000-00000000000c");
    private static readonly Guid Communication = Guid.Parse("15500000-0000-0000-0000-00000000000d");
    private static readonly Guid BusinessUnit = Guid.Parse("15500000-0000-0000-0000-00000000000e");

    /// <summary>Every drive id an upload reached, in order. Cleared by the test class constructor.</summary>
    public ConcurrentQueue<string> Uploads { get; } = new();

    /// <summary>Task 156: every stale row the resolver enqueued for re-stamping (no Service Bus is reached).</summary>
    internal RecordingRestampQueue RestampQueue { get; } = new();

    public HttpClient Client()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            // The caller may append to every record: authorization has its own suite above; placement is
            // what this host tests.
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddSingleton<CallerRecordAccessProbe>(new GrantingProbe());

            services.RemoveAll<ISecurableEntityRegistry>();
            services.AddSingleton(BuildRegistry());

            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(BuildRows());

            // SCOPED: SpeFileStore's constructor dependencies are scoped (see ShareLinkTestFixture for the trap).
            services.RemoveAll<SpeFileStore>();
            services.AddScoped<SpeFileStore>(sp => new RecordingSpeFileStore(sp, Uploads));

            // Task 156: where a container_ancestor_stale refusal enqueues the stale row.
            services.RemoveAll<CoreAncestorRestampQueue>();
            services.AddSingleton<CoreAncestorRestampQueue>(RestampQueue);
        });
    }

    private static ISecurableEntityRegistry BuildRegistry()
    {
        var securable = new HashSet<string>(StringComparer.Ordinal) { "sprk_project", "sprk_matter", "sprk_workassignment" };
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "sprk_project", "sprk_matter", "sprk_workassignment", "sprk_servicerequest", "sprk_todo", "sprk_event",
            "sprk_invoice", "sprk_communication", "contact", "businessunit", "sprk_agreement", "sprk_recordtype_ref",
            // Task 156 verifier round 2 (V3): an analysis and the document it analyses.
            "sprk_analysis", "sprk_document"
        };

        var registry = Substitute.For<ISecurableEntityRegistry>();
        registry.ClassifyEntityAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(TestEntityCatalog.Classify(call.Arg<string>(), securable, known)));
        registry.GetSecurableEntitiesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(securable));
        return registry;
    }

    private static IGenericEntityService BuildRows()
    {
        var bu = new EntityReference("businessunit", BusinessUnit);
        var rows = new Dictionary<(string, Guid), Entity>
        {
            [("sprk_todo", TodoUnderSecureProject)] = new("sprk_todo", TodoUnderSecureProject)
            {
                ["owningbusinessunit"] = bu,
                ["sprk_regardingproject"] = new EntityReference("sprk_project", SecureProject)
            },
            [("sprk_todo", TodoUnderPlainProject)] = new("sprk_todo", TodoUnderPlainProject)
            {
                ["owningbusinessunit"] = bu,
                ["sprk_regardingproject"] = new EntityReference("sprk_project", PlainProject)
            },
            [("sprk_todo", TodoUnderCommunication)] = new("sprk_todo", TodoUnderCommunication)
            {
                ["owningbusinessunit"] = bu,
                ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", Communication),
                ["sprk_regardingproject"] = new EntityReference("sprk_project", PlainProject),
                ["sprk_regardingrecordid"] = Communication.ToString()
            },
            // Task 156: the communication was re-filed to the SECURE project — the to-do above still copies the plain one.
            [("sprk_communication", Communication)] = new("sprk_communication", Communication)
            {
                ["sprk_regardingproject"] = new EntityReference("sprk_project", SecureProject)
            },
            [("sprk_todo", TodoUnderCommunicationFreshCopy)] = new("sprk_todo", TodoUnderCommunicationFreshCopy)
            {
                ["owningbusinessunit"] = bu,
                ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", Communication),
                ["sprk_regardingproject"] = new EntityReference("sprk_project", SecureProject),
                ["sprk_regardingrecordid"] = Communication.ToString()
            },
            // Task 156 verifier round 1 item 7: the NON-secure half of AC4 — a communication filed under the plain
            // project, and a to-do filed under it whose copy equals that live root.
            [("sprk_communication", CommunicationUnderPlainProject)] = new("sprk_communication", CommunicationUnderPlainProject)
            {
                ["sprk_regardingproject"] = new EntityReference("sprk_project", PlainProject)
            },
            [("sprk_todo", TodoUnderPlainCommunicationFreshCopy)] = new("sprk_todo", TodoUnderPlainCommunicationFreshCopy)
            {
                ["owningbusinessunit"] = bu,
                ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", CommunicationUnderPlainProject),
                ["sprk_regardingproject"] = new EntityReference("sprk_project", PlainProject),
                ["sprk_regardingrecordid"] = CommunicationUnderPlainProject.ToString()
            },
            // Task 156 verifier round 2, V2: a communication whose ONLY link is the pair naming the SECURE project (live:
            // 161 of 276 communications carry the pair), and a to-do filed under it — no typed root on either row, so
            // the to-do's (empty) copy is fresh.
            [("sprk_communication", CommunicationPairedToSecureProject)] = new("sprk_communication", CommunicationPairedToSecureProject)
            {
                ["sprk_regardingrecordid"] = SecureProject.ToString("D").ToUpperInvariant(),
                ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", ProjectTypeRef)
            },
            [("sprk_todo", TodoUnderCommunicationPairedToSecureProject)] = new("sprk_todo", TodoUnderCommunicationPairedToSecureProject)
            {
                ["owningbusinessunit"] = bu,
                ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", CommunicationPairedToSecureProject),
                ["sprk_regardingrecordid"] = CommunicationPairedToSecureProject.ToString()
            },
            // Task 156 verifier round 2, V3: an analysis filed under the PLAIN project whose input document belongs to the
            // SECURE project, and a to-do filed under the analysis whose copy (the plain project) is fresh.
            [("sprk_analysis", AnalysisUnderPlainProject)] = new("sprk_analysis", AnalysisUnderPlainProject)
            {
                ["sprk_regardingproject"] = new EntityReference("sprk_project", PlainProject),
                ["sprk_documentid"] = new EntityReference("sprk_document", DocumentUnderSecureProject)
            },
            [("sprk_document", DocumentUnderSecureProject)] = new("sprk_document", DocumentUnderSecureProject)
            {
                ["sprk_project"] = new EntityReference("sprk_project", SecureProject)
            },
            [("sprk_todo", TodoUnderAnalysisOfASecureDocument)] = new("sprk_todo", TodoUnderAnalysisOfASecureDocument)
            {
                ["owningbusinessunit"] = bu,
                ["sprk_regardinganalysis"] = new EntityReference("sprk_analysis", AnalysisUnderPlainProject),
                ["sprk_regardingproject"] = new EntityReference("sprk_project", PlainProject),
                ["sprk_regardingrecordid"] = AnalysisUnderPlainProject.ToString()
            },
            // Task 156 verifier round 2, V1: a to-do filed under the plain-project communication (the pair names it; its
            // copy is fresh) that ALSO names an event of the SECURE project — a carrier.
            [("sprk_event", EventUnderSecureProject)] = new("sprk_event", EventUnderSecureProject)
            {
                ["sprk_regardingproject"] = new EntityReference("sprk_project", SecureProject)
            },
            [("sprk_todo", TodoUnderCommunicationCarryingSecureEvent)] = new("sprk_todo", TodoUnderCommunicationCarryingSecureEvent)
            {
                ["owningbusinessunit"] = bu,
                ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", CommunicationUnderPlainProject),
                ["sprk_regardingevent"] = new EntityReference("sprk_event", EventUnderSecureProject),
                ["sprk_regardingproject"] = new EntityReference("sprk_project", PlainProject),
                ["sprk_regardingrecordid"] = CommunicationUnderPlainProject.ToString()
            },
            [("sprk_todo", TodoUnderUnprovisionedSecureProject)] = new("sprk_todo", TodoUnderUnprovisionedSecureProject)
            {
                ["owningbusinessunit"] = bu,
                ["sprk_regardingproject"] = new EntityReference("sprk_project", UnprovisionedSecureProject)
            },
            [("sprk_project", SecureProject)] = new("sprk_project", SecureProject)
            {
                ["sprk_issecure"] = true,
                ["sprk_containerid"] = SecureProjectContainer
            },
            [("sprk_project", PlainProject)] = new("sprk_project", PlainProject) { ["sprk_issecure"] = false },
            [("sprk_project", UnprovisionedSecureProject)] = new("sprk_project", UnprovisionedSecureProject)
            {
                ["sprk_issecure"] = true
            },
            [("businessunit", BusinessUnit)] = new("businessunit", BusinessUnit)
            {
                ["sprk_containerid"] = BusinessUnitContainer
            },
            [("sprk_event", EventInBusinessUnitWithoutContainer)] = new("sprk_event", EventInBusinessUnitWithoutContainer)
            {
                ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitWithoutContainer)
            },
            [("businessunit", BusinessUnitWithoutContainer)] = new("businessunit", BusinessUnitWithoutContainer),
            // Task 155 f3: every typed regarding NULL, the polymorphic pair naming the SECURE project (live shape:
            // a STRING id, upper-case, plus a sprk_recordtype_ref lookup).
            [("sprk_todo", TodoLinkedOnlyByPairToSecureProject)] = new("sprk_todo", TodoLinkedOnlyByPairToSecureProject)
            {
                ["owningbusinessunit"] = bu,
                ["sprk_regardingrecordid"] = SecureProject.ToString("D").ToUpperInvariant(),
                ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", ProjectTypeRef)
            },
            [("sprk_recordtype_ref", ProjectTypeRef)] = new("sprk_recordtype_ref", ProjectTypeRef)
            {
                ["sprk_recordlogicalname"] = "sprk_project"
            },
            // Task 155 f3 item 1: an invoice regarding an agreement, typed matter/project NULL.
            [("sprk_invoice", InvoiceRegardingAgreement)] = new("sprk_invoice", InvoiceRegardingAgreement)
            {
                ["owningbusinessunit"] = bu,
                ["sprk_regardingagreement"] = new EntityReference("sprk_agreement", Agreement)
            },
            // Task 155 f4: an event under a NON-secure work assignment whose own row regards the SECURE project.
            [("sprk_event", EventUnderWorkAssignmentUnderSecureProject)] = new("sprk_event", EventUnderWorkAssignmentUnderSecureProject)
            {
                ["owningbusinessunit"] = bu,
                ["sprk_regardingworkassignment"] = new EntityReference("sprk_workassignment", WorkAssignmentUnderSecureProject)
            },
            [("sprk_workassignment", WorkAssignmentUnderSecureProject)] = new("sprk_workassignment", WorkAssignmentUnderSecureProject)
            {
                ["sprk_issecure"] = false,
                ["sprk_regardingproject"] = new EntityReference("sprk_project", SecureProject)
            },
            // Task 155 f4: the live a30254d0 → 9c0254d0 shape — the work assignment's only link is a pair naming a
            // record that was deleted (the pair is a STRING; nothing clears it).
            [("sprk_event", EventUnderWorkAssignmentWithDanglingPair)] = new("sprk_event", EventUnderWorkAssignmentWithDanglingPair)
            {
                ["owningbusinessunit"] = bu,
                ["sprk_regardingworkassignment"] = new EntityReference("sprk_workassignment", WorkAssignmentWithDanglingPair)
            },
            [("sprk_workassignment", WorkAssignmentWithDanglingPair)] = new("sprk_workassignment", WorkAssignmentWithDanglingPair)
            {
                // Task 150: a securable root reads No, never absent, once the NULL backfill has run (an absent flag refuses).
                ["sprk_issecure"] = false,
                ["sprk_regardingrecordid"] = DeletedProject.ToString("D").ToUpperInvariant(),
                ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", ProjectTypeRef)
            },
        };

        var service = Substitute.For<IGenericEntityService>();
        service.RetrieveAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var key = (call.ArgAt<string>(0), call.ArgAt<Guid>(1));
                if (key == ("sprk_todo", TodoWhoseRowCannotBeRead))
                {
                    throw new TimeoutException(UnreadableRowFaultText);
                }

                if (key == ("sprk_project", DeletedProject))
                {
                    // Dataverse 0x80040217 ObjectDoesNotExist — what a deleted record's id reads as.
                    throw new System.ServiceModel.FaultException<OrganizationServiceFault>(
                        new OrganizationServiceFault { ErrorCode = -2147220969 },
                        new System.ServiceModel.FaultReason("sprk_project does not exist"));
                }

                return rows.TryGetValue(key, out var row)
                    ? Task.FromResult(row)
                    : throw new InvalidOperationException($"Unmodelled read: {key.Item1} {key.Item2}");
            });
        return service;
    }

    private sealed class GrantingProbe : CallerRecordAccessProbe
    {
        public GrantingProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => Task.FromResult(OperationAccessPolicy.GetRequiredRights(
                RecordRouteAccessAuthorizationFilter.AssociateContentOperation));
    }

    private sealed class RecordingSpeFileStore : SpeFileStore
    {
        private readonly ConcurrentQueue<string> _uploads;

        public RecordingSpeFileStore(IServiceProvider sp, ConcurrentQueue<string> uploads)
            : base(sp.GetRequiredService<ContainerOperations>(),
                   sp.GetRequiredService<DriveItemOperations>(),
                   sp.GetRequiredService<UploadSessionManager>(),
                   sp.GetRequiredService<UserOperations>())
        {
            _uploads = uploads;
        }

        // Task 171: the record-keyed routes write APP-ONLY (the record filter decided; the container came from the
        // record). The OBO members are no longer on the facade, so a route that regressed to OBO would not compile.
        public override Task<FileHandleDto?> UploadSmallAsync(
            string driveId,
            string path,
            Stream content,
            ConflictBehavior conflictBehavior,
            CancellationToken ct = default)
        {
            _uploads.Enqueue(driveId);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult<FileHandleDto?>(new FileHandleDto(
                "item-155", path, null, 3, now, now, null, false, null, driveId));
        }

        public override Task<UploadSessionResponse?> CreateUploadSessionAsync(
            string driveId,
            string path,
            ConflictBehavior conflictBehavior,
            CancellationToken ct = default)
        {
            _uploads.Enqueue("session:" + driveId);
            return Task.FromResult<UploadSessionResponse?>(
                new UploadSessionResponse("https://example.invalid/upload-session", DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
}
