using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 031 (FR-19): POST /api/v1/events and POST /api/ai/analysis/create create their
/// row through the ONE server create (<see cref="RecordCreationService"/>, as the caller). Each test runs the REAL route
/// handler over the REAL service and the harness's own collaborators, and reads what reached the single app-only create
/// (<c>_appCreates</c>): the record-first owner, the creator person, the I-1 stamp and the route's own values. The route
/// FILTERS are unchanged and covered by the Authorization contract suites; the refusals below pin NFR-07 (status, words,
/// errorCode unchanged) and NFR-01 (a refusal creates nothing, and runs no post-create step).
/// </summary>
/// <remarks>A partial of <see cref="SecureChildOwnershipAiToolTests"/> (its route harness), as RouteCreatePreCreateStampTests is.</remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid TypedRouteEventId = Guid.Parse("a2460000-0000-4000-8000-000000031001");
    private static readonly Guid TypedRouteAnalysis = Guid.Parse("a2460000-0000-4000-8000-000000031002");

    private sealed record TypedEventRun(IResult Result, Mock<IEventDataverseService> Events, Mock<IGenericEntityService> Entities);

    private RecordCreationService TypedRouteService(IRecordOwnershipResolver ownership, StampWorld? stamps = null) =>
        RouteRecordCreation.Over(
            ownership, _appOnly.Object, (stamps ?? new StampWorld()).Restamper, Shares(),
            SecureRootFilingGateFixtures.NothingSecure(), null, _routeMembership.Object);

    private async Task<TypedEventRun> RunTypedEventRoute(
        ApiCreateEventRequest request, IRecordOwnershipResolver? ownership = null, StampWorld? stamps = null, Guid? linkedContact = null)
    {
        var events = new Mock<IEventDataverseService>(MockBehavior.Strict);
        events.Setup(e => e.CreateEventLogAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        var callers = new Mock<ICallerSystemUserResolver>();
        callers.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(Caller.ToString("D")));
        var identity = new Mock<IIdentityNormalizationService>();
        identity.Setup(i => i.ResolveAsync(Caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(Caller, ContactId: linkedContact));
        var entities = RouteRecordCreation.Entities();

        var result = await EventEndpoints.CreateEventAsync(
            request,
            TypedRouteService(ownership ?? _world.Resolver(), stamps),
            _user,
            events.Object,
            entities.Object,
            callers.Object,
            identity.Object,
            Mock.Of<ICommunicationDataverseService>(),
            HttpContextOfCaller(),
            NullLogger<Program>.Instance,
            CancellationToken.None);
        return new TypedEventRun(result, events, entities);
    }

    /// <summary>A bind of the create body as text: the service writes the owner as a string and a mapped lookup as a JSON string.</summary>
    private static string? TypedBind(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var value)
            ? value as string ?? (value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : null)
            : null;

    /// <summary>A plain column of the create body as its JSON text (the service writes plain values as JSON elements).</summary>
    private static string? PlainJson(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var value) ? JsonSerializer.Serialize(value) : null;

    private void TheOrdinaryMatterAnalysisExists()
    {
        _world.WithRecord("sprk_analysis", TypedRouteAnalysis, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_analysis", TypedRouteAnalysis, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
    }

    // =====================================================================================================================
    // POST /api/v1/events
    // =====================================================================================================================

    [Fact]
    public async Task TypedEventRoute_CreatesThroughTheService_OwnedByTheRegardingsTeam_WithTheCreatorPerson_AndTheRoutesOwnValues()
    {
        var run = await RunTypedEventRoute(
            new ApiCreateEventRequest("Hearing prep", Description: "Bring exhibits", RegardingRecordId: OrdinaryMatter,
                RegardingRecordType: RegardingRecordType.Matter, DueDate: new DateOnly(2026, 12, 1), Priority: 100000002),
            linkedContact: CallerContact);

        Status(run.Result).Should().Be(StatusCodes.Status201Created, Detail(run.Result));
        var (table, id, fields) = _appCreates.Should().ContainSingle("the one app-only create").Subject;
        table.Should().Be("sprk_event");
        TypedBind(fields, "ownerid@odata.bind").Should().Be($"/teams({Directory.ChildTeam:D})", "record-first: the regarding matter's team");
        TypedBind(fields, "sprk_CreatedByPerson@odata.bind").Should().Be($"/systemusers({Caller:D})", "the person who asked");
        TypedBind(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})");
        TypedBind(fields, "sprk_AssignedTo@odata.bind").Should().Be($"/contacts({CallerContact:D})", "owner S1: the acting user's linked contact");
        PlainJson(fields, "statuscode").Should().Be(EventStatusCode.Open.ToString());
        PlainJson(fields, "statecode").Should().Be(EventStatusCode.GetStateCode(EventStatusCode.Open).ToString());
        PlainJson(fields, "sprk_eventname").Should().Be("\"Hearing prep\"");
        PlainJson(fields, "sprk_duedate").Should().Be("\"2026-12-01\"", "a Date Only column takes the calendar date");
        PlainJson(fields, "sprk_regardingrecordid").Should().Be("\"" + OrdinaryMatter.ToString("D") + "\"");
        _user.Posts.Should().BeEmpty("the row is never created as the user (ADR-028)");
        run.Events.Verify(e => e.CreateEventLogAsync(
            id, EventLogAction.Created, It.IsAny<string?>(), Directory.ChildTeam, Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TypedEventRoute_UnderAnAnalysis_CarriesTheI1StampOfItsMatter()
    {
        TheOrdinaryMatterAnalysisExists();
        var stamps = new StampWorld().Row("sprk_matter", OrdinaryMatter)
            .Row("sprk_analysis", TypedRouteAnalysis, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)]);

        var run = await RunTypedEventRoute(
            new ApiCreateEventRequest("Review deadline", RegardingRecordId: TypedRouteAnalysis, RegardingRecordType: RegardingRecordType.Analysis),
            stamps: stamps);

        Status(run.Result).Should().Be(StatusCodes.Status201Created, Detail(run.Result));
        var (_, _, fields) = _appCreates.Should().ContainSingle().Subject;
        TypedBind(fields, "sprk_RegardingAnalysis@odata.bind").Should().Be($"/sprk_analysises({TypedRouteAnalysis:D})");
        TypedBind(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})",
            "FR-41: the I-1 stamp is derived by the service from what the event is filed under, not by the endpoint");
    }

    [Fact]
    public async Task TypedEventRoute_WithNoLinkedContact_LeavesAssignedToBlank_AndStillCreates()
    {
        var run = await RunTypedEventRoute(new ApiCreateEventRequest("Standalone"), linkedContact: null);

        Status(run.Result).Should().Be(StatusCodes.Status201Created, Detail(run.Result));
        _appCreates.Should().ContainSingle().Which.Fields.Keys.Should().NotContain(k => k.StartsWith("sprk_AssignedTo", StringComparison.OrdinalIgnoreCase),
            "no link, blank; never an email match");
    }

    [Fact]
    public async Task TypedEventRoute_PublishesTheOwnerMembershipEventOnce_AfterTheCreate()
    {
        var run = await RunTypedEventRoute(new ApiCreateEventRequest("Standalone"));

        Status(run.Result).Should().Be(StatusCodes.Status201Created, Detail(run.Result));
        _routeMembership.Verify(
            p => p.PublishAsync(It.Is<MembershipChangedEvent>(e => e.EntityLogicalName == "sprk_event"), It.IsAny<CancellationToken>()),
            Times.Once, "the service publishes it (step 12); the endpoint no longer does, so it is never published twice");
    }

    [Fact]
    public async Task TypedEventRoute_WhenNoOwnerResolves_Is409WithTheSameBody_CreatesNothing_PublishesNothing_LogsNothing()
    {
        var ownership = new RecordOwnershipResolverDouble { TeamId = null, RefusalCode = RecordOwnerRefusal.SecureParentNotIsolated };

        var run = await RunTypedEventRoute(new ApiCreateEventRequest("Standalone"), ownership);

        Status(run.Result).Should().Be(StatusCodes.Status409Conflict);
        var problem = run.Result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;
        problem.Title.Should().Be("Record owner unresolved");
        problem.Detail.Should().StartWith("The event was not saved: ");
        problem.Extensions["reasonCode"].Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        _appCreates.Should().BeEmpty();
        _routeMembership.Invocations.Should().BeEmpty("a refusal publishes no membership event");
        run.Events.Verify(e => e.CreateEventLogAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TypedEventRoute_WhenTheCallerLacksTheCreatePrivilege_RefusesBeforeAnythingIsCreated_FailClosed()
    {
        _user.Held.Remove("prvCreatesprk_event");

        var run = await RunTypedEventRoute(new ApiCreateEventRequest("Standalone"));

        Status(run.Result).Should().Be(StatusCodes.Status500InternalServerError,
            "a refusal the route never gave before fails closed to its existing generic failure (NFR-07)");
        (run.Result as ProblemHttpResult)!.ProblemDetails.Detail.Should().Be("An error occurred while creating the event");
        _appCreates.Should().BeEmpty();
        run.Events.Verify(e => e.CreateEventLogAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =====================================================================================================================
    // POST /api/ai/analysis/create
    // =====================================================================================================================

    private async Task<(IResult Result, Mock<IAnalysisDataverseService> Analyses)> RunTypedAnalysisRoute(
        IRecordOwnershipResolver? ownership = null, Guid[]? skills = null, Guid? document = null)
    {
        var analyses = new Mock<IAnalysisDataverseService>(MockBehavior.Strict);
        analyses.Setup(a => a.AssociateScopesAsync(
                It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await AnalysisEndpoints.CreateAnalysis(
            new CreateAnalysisRequest("Analysis - NDA.pdf", document ?? OrdinaryDocument, skills ?? [], [], []),
            TypedRouteService(ownership ?? _world.Resolver()),
            _user,
            analyses.Object,
            NullLogger<AnalysisOrchestrationService>.Instance,
            HttpContextOfCaller(),
            CancellationToken.None);
        return (result, analyses);
    }

    [Fact]
    public async Task TypedAnalysisRoute_CreatesThroughTheService_OwnedByTheDocumentsTeam_AnchoredToIt_ThenAssociatesTheScopes()
    {
        var skill = Guid.NewGuid();

        var (result, analyses) = await RunTypedAnalysisRoute(skills: [skill]);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var (table, id, fields) = _appCreates.Should().ContainSingle("the one app-only create").Subject;
        table.Should().Be("sprk_analysis");
        TypedBind(fields, "ownerid@odata.bind").Should().Be($"/teams({Directory.ChildTeam:D})", "record-first: the document's team");
        TypedBind(fields, "sprk_CreatedByPerson@odata.bind").Should().Be($"/systemusers({Caller:D})");
        TypedBind(fields, "sprk_DocumentId@odata.bind").Should().Be($"/sprk_documents({OrdinaryDocument:D})", "the document is the analysis's anchor");
        PlainJson(fields, "sprk_name").Should().Be("\"Analysis - NDA.pdf\"");
        PlainJson(fields, "statuscode").Should().Be("1");
        _user.Posts.Should().BeEmpty("the row is never created as the user (ADR-028)");
        analyses.Verify(a => a.AssociateScopesAsync(
            id, It.Is<IEnumerable<Guid>>(s => s.Single() == skill), It.IsAny<IEnumerable<Guid>>(), It.IsAny<IEnumerable<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Once, "the N:N association is the route's, after a successful create");
    }

    [Fact]
    public async Task TypedAnalysisRoute_WhenNoOwnerResolves_Is409_CreatesNothing_AndNeverAssociatesScopes()
    {
        var ownership = new RecordOwnershipResolverDouble { TeamId = null, RefusalCode = RecordOwnerRefusal.SecureParentNotIsolated };

        var (result, analyses) = await RunTypedAnalysisRoute(ownership, skills: [Guid.NewGuid()]);

        Status(result).Should().Be(StatusCodes.Status409Conflict);
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;
        problem.Title.Should().Be("Record owner unresolved");
        problem.Detail.Should().StartWith("The analysis was not saved: ");
        problem.Extensions["reasonCode"].Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        _appCreates.Should().BeEmpty();
        analyses.Verify(a => a.AssociateScopesAsync(
            It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<IEnumerable<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Never, "NFR-01: the association must not run when the create refused");
    }

    [Fact]
    public async Task TypedAnalysisRoute_WhenTheCallerLacksTheCreatePrivilege_FailsClosedToTheGeneric500_AndAssociatesNothing()
    {
        _user.Held.Remove("prvCreatesprk_analysis");

        var (result, analyses) = await RunTypedAnalysisRoute(skills: [Guid.NewGuid()]);

        Status(result).Should().Be(StatusCodes.Status500InternalServerError);
        (result as ProblemHttpResult)!.ProblemDetails.Detail.Should().Be("Failed to create analysis record.");
        _appCreates.Should().BeEmpty();
        analyses.Invocations.Should().BeEmpty();
    }
}
