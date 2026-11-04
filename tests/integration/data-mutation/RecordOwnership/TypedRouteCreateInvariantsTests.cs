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
        IRecordOwnershipResolver? ownership = null, Guid[]? skills = null, Guid? document = null, Guid? playbook = null,
        Guid? filterAuthorisedPlaybook = null)
    {
        // Task 031 fix round 1: the /create filter's playbook-use record, as AnalysisAuthorizationFilter writes it after every
        // check passed (the filter itself is the contract suite's subject; here only its recorded decision is stated).
        var http = HttpContextOfCaller();
        if (filterAuthorisedPlaybook is { } authorised)
            Sprk.Bff.Api.Api.Filters.AnalysisAuthorizationFilter.RecordPlaybookUseAuthorized(http, authorised);

        var analyses = new Mock<IAnalysisDataverseService>(MockBehavior.Strict);
        analyses.Setup(a => a.AssociateScopesAsync(
                It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await AnalysisEndpoints.CreateAnalysis(
            new CreateAnalysisRequest("Analysis - NDA.pdf", document ?? OrdinaryDocument, skills ?? [], [], [], PlaybookId: playbook),
            TypedRouteService(ownership ?? _world.Resolver()),
            _user,
            analyses.Object,
            NullLogger<AnalysisOrchestrationService>.Instance,
            http,
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

    // =====================================================================================================================
    // Task 031 fix round 1 (F1 / F3; UAC-r2 conditions (b) to (g)): the playbook the route filter authorised
    // =====================================================================================================================

    private static readonly Guid TypedRoutePlaybook = Guid.Parse("a2460000-0000-4000-8000-000000031003");

    /// <summary>Every path the caller's Dataverse client was asked (the harness's GET override, answering nothing itself).</summary>
    private List<string> RecordCallerReads()
    {
        var paths = new List<string>();
        _user.GetOverride = path =>
        {
            paths.Add(path);
            return null;
        };
        return paths;
    }

    private static bool AsksRightsOn(IEnumerable<string> paths, string entitySet, Guid id) =>
        paths.Any(p => p.Contains("RetrievePrincipalAccess", StringComparison.Ordinal) && p.Contains($"{entitySet}({id:D})", StringComparison.Ordinal));

    [Fact]
    public async Task TypedAnalysisRoute_AnOrdinaryUser_ReadButNoAppendToOnThePlaybook_CreatesFromThePlaybookTheFilterAuthorised_201()
    {
        // F1 (HIGH): the shipped roles give an ordinary user Read and no AppendTo on sprk_analysisplaybook (the harness's default,
        // ScriptedUserClient.TableRights). The filter's playbook-use decision authorised this playbook, so the service asks its
        // Read (existence) and not its AppendTo, and the create stands.
        var paths = RecordCallerReads();

        var (result, _) = await RunTypedAnalysisRoute(playbook: TypedRoutePlaybook, filterAuthorisedPlaybook: TypedRoutePlaybook);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var (_, _, fields) = _appCreates.Should().ContainSingle().Subject;
        TypedBind(fields, "sprk_Playbook@odata.bind").Should().Be($"/sprk_analysisplaybooks({TypedRoutePlaybook:D})",
            "the caller's playbook is written as they sent it");
        AsksRightsOn(paths, "sprk_analysisplaybooks", TypedRoutePlaybook).Should().BeTrue(
            "UAC-r2 (d): the caller's Read on the playbook is still asked, as the caller");
    }

    [Fact]
    public async Task TypedAnalysisRoute_ThePlaybookWithoutTheFiltersRecord_KeepsItsAppendTo_AndIsRefused()
    {
        // UAC-r2 (f), must not fire: the same column, no route decision recorded (the option is not set) - AppendTo is asked
        // as for any caller lookup, and an ordinary user does not hold it.
        var (result, analyses) = await RunTypedAnalysisRoute(playbook: TypedRoutePlaybook, filterAuthorisedPlaybook: null);

        Status(result).Should().Be(StatusCodes.Status500InternalServerError);
        (result as ProblemHttpResult)!.ProblemDetails.Detail.Should().Be("Failed to create analysis record.");
        _appCreates.Should().BeEmpty();
        analyses.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task TypedAnalysisRoute_TheFilterAuthorisedAnotherPlaybook_ThisOnesAppendToIsAsked_AndIsRefused()
    {
        // UAC-r2 (c), and the (column, id) change of 2026-11-04, must not fire: the option carries playbook A (the filter's
        // decision, which the handler passes as it is) and the payload carries B. Only A would be exempt, so B's AppendTo is
        // asked as for any caller lookup - and an ordinary user (Read, no AppendTo) is refused. B's Read is granted, so the
        // refusal is the AppendTo question, not existence.
        var decided = Guid.Parse("a2460000-0000-4000-8000-0000000031aa");
        var paths = RecordCallerReads();

        var (result, analyses) = await RunTypedAnalysisRoute(playbook: TypedRoutePlaybook, filterAuthorisedPlaybook: decided);

        Status(result).Should().Be(StatusCodes.Status500InternalServerError);
        (result as ProblemHttpResult)!.ProblemDetails.Detail.Should().Be("Failed to create analysis record.");
        _appCreates.Should().BeEmpty();
        analyses.Invocations.Should().BeEmpty();
        AsksRightsOn(paths, "sprk_analysisplaybooks", TypedRoutePlaybook).Should().BeTrue("B's AppendTo is the caller's question");
        AsksRightsOn(paths, "sprk_analysisplaybooks", decided).Should().BeFalse("A is not in the payload: nothing is asked of it");
    }

    [Fact]
    public async Task TypedAnalysisRoute_WithThePlaybookAuthorised_AnotherLookupIsStillAskedItsAppendTo()
    {
        // UAC-r2 (f), must not fire: the option names the playbook only; the document's AppendTo is still the caller's question.
        _user.NoAppendToIn.Add(("sprk_documents", OrdinaryDocument));

        var (result, analyses) = await RunTypedAnalysisRoute(playbook: TypedRoutePlaybook, filterAuthorisedPlaybook: TypedRoutePlaybook);

        Status(result).Should().Be(StatusCodes.Status500InternalServerError);
        _appCreates.Should().BeEmpty();
        analyses.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unreadable")]
    public async Task TypedAnalysisRoute_AnAuthorisedPlaybookTheCallerCannotRead_IsTheSameRefusal_NothingCreated(string playbookCase)
    {
        // UAC-r2 (d): only AppendTo is skipped - existence and Read are still asked as the caller, and a missing row and an
        // unreadable one answer alike (the service's uniform not-found, which this route renders as its generic 500).
        if (playbookCase == "missing")
            _user.Missing.Add(TypedRoutePlaybook);
        else
            _user.TableRights["sprk_analysisplaybooks"] = "None";

        var (result, _) = await RunTypedAnalysisRoute(playbook: TypedRoutePlaybook, filterAuthorisedPlaybook: TypedRoutePlaybook);

        Status(result).Should().Be(StatusCodes.Status500InternalServerError);
        (result as ProblemHttpResult)!.ProblemDetails.Detail.Should().Be("Failed to create analysis record.");
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task TypedEventRoute_ACoreUser_CreatesWithAnEventType()
    {
        // F3: the event type keeps its AppendTo (no route decision on it). A Core User holds prvAppendTosprk_EventType_Ref (Global).
        var eventType = Guid.NewGuid();

        var run = await RunTypedEventRoute(new ApiCreateEventRequest("Standalone", EventTypeId: eventType));

        Status(run.Result).Should().Be(StatusCodes.Status201Created, Detail(run.Result));
        TypedBind(_appCreates.Should().ContainSingle().Subject.Fields, "sprk_EventType_Ref@odata.bind")
            .Should().Be($"/sprk_eventtype_refs({eventType:D})");
    }

    [Fact]
    public async Task TypedEventRoute_ABasicUser_WithoutAppendToOnTheEventType_IsRefused_KnownLimitK1()
    {
        // F3 / review K1 (owner 2026-11-04: a roles-alignment task, not an exemption): Spaarke Basic User holds Read and no
        // AppendTo on sprk_eventtype_ref, and the event route makes no route-level decision on the event type, so its AppendTo
        // is asked and refused. Pinned so the roles task flips it deliberately.
        _user.TableRights["sprk_eventtype_refs"] = "ReadAccess";

        var run = await RunTypedEventRoute(new ApiCreateEventRequest("Standalone", EventTypeId: Guid.NewGuid()));

        Status(run.Result).Should().Be(StatusCodes.Status500InternalServerError);
        (run.Result as ProblemHttpResult)!.ProblemDetails.Detail.Should().Be("An error occurred while creating the event");
        _appCreates.Should().BeEmpty();
    }

    // ── UAC-r2 (b), (e): a closed list; a filing lookup or an ownership-parent target is the adapter's defect ──────────────

    private static RecordCreationRequest RouteAuthorised(string table, params string[] columns) =>
        RouteAuthorisedFor(table, columns.ToDictionary(c => c, _ => TypedRoutePlaybook));

    private static RecordCreationRequest RouteAuthorisedFor(string table, IReadOnlyDictionary<string, Guid> lookups) =>
        new(table, new RecordCreationPayload.WebApi(JsonSerializer.SerializeToElement(new { })))
        {
            RouteAuthorizedLookups = lookups,
        };

    private RecordCreationPrincipal TheCaller() => RecordCreationPrincipal.ForCaller(_user, null);

    [Theory]
    [InlineData("sprk_todo", "sprk_regardingmatter", "files")]               // a lineage (SecureChildLineage) filing lookup
    [InlineData("sprk_event", "sprk_regardingevent", "files")]               // an ownership-parent filing lookup (an intermediate)
    [InlineData("sprk_workassignment", "sprk_regardingmatter", "files")]     // a secure root's filing column
    [InlineData("sprk_event", "sprk_eventtype_ref", "closed list")]          // a configuration lookup no route decision covers
    [InlineData("sprk_analysis", "sprk_actionid", "closed list")]            // another analysis configuration lookup
    public void RouteAuthorisedLookups_AnythingButTheListedPlaybook_IsAnArgumentError(string table, string column, string why)
    {
        var act = () => RecordCreationService.ValidateRouteAuthorizedLookups(table, RouteAuthorised(table, column), TheCaller());

        act.Should().Throw<ArgumentException>().WithMessage($"*{why}*");
    }

    [Fact]
    public void RouteAuthorisedLookups_AListedLookupToAnOwnershipParentTable_IsAnArgumentError()
    {
        // The ownership-parent class: even an entry on the list, on a column no filing list names (the reviewer lookup), may not
        // name an ownership-parent table (a matter here).
        var act = () => RecordCreationService.ValidateRouteAuthorizedLookups(
            "sprk_analysis", RouteAuthorised("sprk_analysis", "sprk_reviewerby"), TheCaller(),
            [("sprk_analysis", "sprk_reviewerby", "sprk_matter")]);

        act.Should().Throw<ArgumentException>().WithMessage("*ownership parent*");
    }

    [Fact]
    public void RouteAuthorisedLookups_TheListedPlaybook_IsAccepted_ButNeverForASystemPrincipal_NorAlsoServerSet()
    {
        var request = RouteAuthorised("sprk_analysis", "SPRK_Playbook ");
        RecordCreationService.ValidateRouteAuthorizedLookups("sprk_analysis", request, TheCaller());
        request.RouteAuthorizedLookups.Should().BeEquivalentTo(
            new Dictionary<string, Guid> { ["sprk_playbook"] = TypedRoutePlaybook }, "the column normalised as the server-set set is; the id kept");

        var system = () => RecordCreationService.ValidateRouteAuthorizedLookups("sprk_analysis", request, RecordCreationPrincipal.ForSystem());
        system.Should().Throw<ArgumentException>().WithMessage("*caller's create only*");

        var both = () => RecordCreationService.ValidateRouteAuthorizedLookups(
            "sprk_analysis", request with { ServerSetLookupColumns = new HashSet<string> { "sprk_playbook" } }, TheCaller());
        both.Should().Throw<ArgumentException>().WithMessage("*only one*");

        // UAC-r2 (2026-11-04): the route names the record it decided; an empty id, or two ids for one column, is a defect.
        var noRecord = () => RecordCreationService.ValidateRouteAuthorizedLookups(
            "sprk_analysis", RouteAuthorisedFor("sprk_analysis", new Dictionary<string, Guid> { ["sprk_playbook"] = Guid.Empty }), TheCaller());
        noRecord.Should().Throw<ArgumentException>().WithMessage("*no record*");
        var twoRecords = () => RouteAuthorisedFor("sprk_analysis",
            new Dictionary<string, Guid> { ["sprk_playbook"] = TypedRoutePlaybook, ["SPRK_PLAYBOOK"] = Guid.NewGuid() });
        twoRecords.Should().Throw<ArgumentException>().WithMessage("*two different records*");
    }

    [Fact]
    public async Task RouteAuthorisedLookups_TheServiceThrowsBeforeAskingAnything()
    {
        var reads = RecordCallerReads();

        var act = () => TypedRouteService(_world.Resolver()).CreateAsync(
            RouteAuthorised("sprk_event", "sprk_eventtype_ref"), TheCaller(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        reads.Should().BeEmpty("the argument error is thrown before anything is read");
        _appCreates.Should().BeEmpty();
    }
}
