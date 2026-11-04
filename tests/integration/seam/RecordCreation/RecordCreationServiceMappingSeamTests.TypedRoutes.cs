using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 031 (spec FR-19, FR-02, FR-03, FR-04) - <c>POST /api/v1/events</c> and
/// <c>POST /api/ai/analysis/create</c> create through the ONE server create and so carry the field mapping of the table's
/// profile. Driven through the REAL route handlers (<see cref="EventEndpoints.CreateEventAsync"/>,
/// <see cref="AnalysisEndpoints.CreateAnalysis"/>) over the REAL <see cref="RecordCreationService"/> of the task 014 harness
/// (the real profile reader, engine, source probe, caller checks and owner resolver; doubled only at the module boundaries).
/// </summary>
/// <remarks>
/// <para>What is pinned: an event filed under a Matter takes the Matter to Event profile's values (the source inferred from
/// what it is filed under, read as the caller); fill-only (FR-02: what the user typed wins); an analysis takes its
/// document's profile the same way; and an event filed under nothing, or whose filing has no profile, is exactly the route
/// it was (no mapped value, no warning). Owner, I-1 stamp and creator person on these two routes are
/// <c>TypedRouteCreateInvariantsTests</c>' (the route harness); the route filters are the Authorization contract suites'.</para>
/// <para>A partial of <see cref="RecordCreationServiceMappingSeamTests"/> (its harness), as the route suites' partials are.</para>
/// </remarks>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    private static readonly Guid TypedRouteDocument = Guid.Parse("a0310000-0000-4000-8000-000000000001");

    /// <summary>The caller as the route handlers see them: an <c>oid</c>, a bearer token, and the probe as the request's own service.</summary>
    private DefaultHttpContext RouteHttpContext()
    {
        var http = new DefaultHttpContext
        {
            TraceIdentifier = FixedCorrelation,
            RequestServices = new ServiceCollection().AddSingleton<CallerRecordAccessProbe>(_probe).BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", CallerOid.ToString("D"))], "test")),
        };
        http.Request.Headers.Authorization = $"Bearer {CallerToken}";
        return http;
    }

    private Task<IResult> RouteCreateEvent(ApiCreateEventRequest request)
    {
        var callers = new Mock<ICallerSystemUserResolver>();
        callers.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(Caller.ToString("D")));
        var events = new Mock<IEventDataverseService>(MockBehavior.Loose);
        events.Setup(e => e.CreateEventLogAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // The regarding record's name and number come from the route's own reads (not the service's): none are scripted here, so
        // the route keeps the request's name and writes no number; the record-type catalog has no row for the matter.
        return EventEndpoints.CreateEventAsync(
            request, Service(), _user, events.Object, new Mock<IGenericEntityService>(MockBehavior.Loose).Object, callers.Object,
            IdentityNormalizationFixtures.NoLinkedContact(), new Mock<ICommunicationDataverseService>(MockBehavior.Loose).Object,
            RouteHttpContext(), NullLogger<Program>.Instance, CancellationToken.None);
    }

    private Task<IResult> RouteCreateAnalysis(Guid document)
    {
        var analyses = new Mock<IAnalysisDataverseService>(MockBehavior.Strict);
        return AnalysisEndpoints.CreateAnalysis(
            new CreateAnalysisRequest("Analysis - NDA.pdf", document, [], [], []),
            Service(), _user, analyses.Object, NullLogger<AnalysisOrchestrationService>.Instance, RouteHttpContext(),
            CancellationToken.None);
    }

    /// <summary>An analysis filed under a document the caller can read, with a profile that copies the document's description.</summary>
    private void TheDocumentOfAnAnalysis()
    {
        _world.WithRecord("sprk_document", TypedRouteDocument, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _stamps.Row("sprk_document", TypedRouteDocument);
        _readable.Add(TypedRouteDocument);
        _sources[("sprk_document", TypedRouteDocument)] = new Entity("sprk_document", TypedRouteDocument)
        {
            ["sprk_documentdescription"] = "From the document",
        };
        _user.Columns["sprk_analysis"] = new()
        {
            ["sprk_name"] = ("String", true),
            ["sprk_description"] = ("Memo", true),
        };
        Profile("sprk_document", "sprk_analysis", Rule("sprk_documentdescription", "sprk_description", Memo));
    }

    // =====================================================================================
    // POST /api/v1/events
    // =====================================================================================

    [Fact]
    public async Task TypedEventRoute_FiledUnderAMatter_TakesTheMatterToEventProfilesValues_ReadAsTheCaller()
    {
        MatterToEventProfile();

        var result = await RouteCreateEvent(new ApiCreateEventRequest(
            "Hearing prep", RegardingRecordId: OrdinaryMatter, RegardingRecordType: Sprk.Bff.Api.Api.Events.Dtos.RegardingRecordType.Matter));

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        var (table, _, fields) = _creates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_event");
        StringOf(fields, "sprk_description").Should().Be("From the matter", "FR-19: the Matter to Event profile fills the untouched column");
        StringOf(fields, "sprk_location").Should().Be("Courtroom 4");
        StringOf(fields, "sprk_eventname").Should().Be("Hearing prep");
        _probe.Asked.Should().Contain((CallerToken, "sprk_matters", OrdinaryMatter), "FR-04: the source is read as the caller, after a Read probe on it");
        _scripted.Posts.Should().BeEmpty("the row is never created as the caller (ADR-028)");
    }

    [Fact]
    public async Task TypedEventRoute_WhatTheUserTypedWins_AFillOnlyMapping()
    {
        MatterToEventProfile();

        var result = await RouteCreateEvent(new ApiCreateEventRequest(
            "Hearing prep", Description: UsersOwnWords, RegardingRecordId: OrdinaryMatter,
            RegardingRecordType: Sprk.Bff.Api.Api.Events.Dtos.RegardingRecordType.Matter));

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be(UsersOwnWords, "FR-02: a caller-supplied value wins");
        StringOf(fields, "sprk_location").Should().Be("Courtroom 4", "an untouched mapped column is still filled");
    }

    [Fact]
    public async Task TypedEventRoute_FiledUnderNothing_NamesNoSource_AndMapsNothing()
    {
        MatterToEventProfile();

        var result = await RouteCreateEvent(new ApiCreateEventRequest("Standalone"));

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Should().NotContain("sprk_description").And.NotContain("sprk_location");
        _journal.Should().NotContain("profile", "an event filed under nothing has no source to infer: a silent no-op");
        _probe.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task TypedEventRoute_FiledUnderAMatterWithNoProfile_IsExactlyTheOldRoute_NoMappedValue_NoWarning()
    {
        // No profile registered for sprk_matter -> sprk_event.
        var result = await RouteCreateEvent(new ApiCreateEventRequest(
            "Hearing prep", RegardingRecordId: OrdinaryMatter, RegardingRecordType: Sprk.Bff.Api.Api.Events.Dtos.RegardingRecordType.Matter));

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Should().NotContain("sprk_description").And.NotContain("sprk_location");
    }

    // =====================================================================================
    // POST /api/ai/analysis/create
    // =====================================================================================

    /// <summary>
    /// The analysis route asks for the mapping of the document it is anchored to (FR-19, FR-03), and the service answers as it
    /// does for every source it cannot read as the caller (FR-04): a <c>sprk_document</c> is not one of the types the caller-read
    /// check can authorize (<c>EntityAccessFilter.TryResolveEntitySet</c> has no <c>sprk_document</c> entry: that map is the
    /// document-ASSOCIATION lockstep map), so nothing is mapped, the source is never read with the app's identity, and the
    /// rest of the create (name, document bind, owner, person) is exactly the route's. Pins the fail-closed outcome so a future
    /// widening of the type map is a visible, deliberate change.
    /// </summary>
    [Fact]
    public async Task TypedAnalysisRoute_AnchoredToADocument_AsksForTheProfile_ButAnUnauthorizableSourceMapsNothing_FailClosed()
    {
        TheDocumentOfAnAnalysis();

        var result = await RouteCreateAnalysis(TypedRouteDocument);

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        var (table, _, fields) = _creates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_analysis");
        _journal.Should().Contain("profile", "FR-19: the route asked for the Document to Analysis profile (the source inferred from the document bind)");
        fields.Keys.Should().NotContain("sprk_description", "FR-04: a source the caller-read check cannot authorize maps nothing");
        _sourceReads.Should().BeEmpty("the document is never read with the app's identity when the caller's right to read it was not established");
        StringOf(fields, "sprk_name").Should().Be("Analysis - NDA.pdf");
        StringOf(fields, "sprk_DocumentId@odata.bind").Should().Be($"/sprk_documents({TypedRouteDocument:D})");
    }

    [Fact]
    public async Task TypedAnalysisRoute_ADocumentWithNoProfile_IsExactlyTheOldRoute_NoMappedValue()
    {
        TheDocumentOfAnAnalysis();
        _profiles.Remove(("sprk_document", "sprk_analysis"));

        var result = await RouteCreateAnalysis(TypedRouteDocument);

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        _creates.Should().ContainSingle().Which.Fields.Keys.Should().NotContain("sprk_description");
    }
}
