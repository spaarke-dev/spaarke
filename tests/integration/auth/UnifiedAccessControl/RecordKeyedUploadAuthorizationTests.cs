using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using NSubstitute;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
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
    private const string UnmappedEntity = "sprk_workassignment";
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
        // sprk_workassignment is a real upload target (CreateWorkAssignmentWizard uploads against it) that
        // is NOT in EntityAccessFilter's logical-name -> entity-set table. It must deny, not proceed: an
        // entity whose per-record access nothing here can evaluate is an entity whose uploads cannot be
        // accepted, because accepting one writes bytes into a container on the strength of no decision.
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
        var securable = new HashSet<string>(StringComparer.Ordinal) { MappedEntity };

        registry.GetSecurableEntitiesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(securable));
        registry.IsSecurableAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(securable.Contains(call.Arg<string>().ToLowerInvariant())));

        return new RecordContainerResolver(
            registry, entityService, NullLogger<RecordContainerResolver>.Instance);
    }

    private static void StubRecordRead(
        IGenericEntityService entityService,
        bool isSecure,
        string? ownContainerId,
        bool withOwningBusinessUnit)
    {
        var row = new Entity(MappedEntity, RecordId) { ["sprk_issecure"] = isSecure };

        if (ownContainerId is not null)
        {
            row["sprk_containerid"] = ownContainerId;
        }

        if (withOwningBusinessUnit)
        {
            row["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId);
        }

        entityService
            .RetrieveAsync(MappedEntity, RecordId, Arg.Any<string[]>(), Arg.Any<CancellationToken>())
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
