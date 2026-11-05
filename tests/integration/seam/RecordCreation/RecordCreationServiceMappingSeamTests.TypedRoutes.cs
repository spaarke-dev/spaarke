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
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.RecordCreation;
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
    private DefaultHttpContext RouteHttpContext(CallerRecordAccessProbe? probe = null)
    {
        var http = new DefaultHttpContext
        {
            TraceIdentifier = FixedCorrelation,
            RequestServices = new ServiceCollection().AddSingleton<CallerRecordAccessProbe>(probe ?? _probe).BuildServiceProvider(),
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

    /// <param name="document">The body document.</param>
    /// <param name="filterAllowed">Task 043: the document the route filter's Document check allowed (Read + AppendTo, then the
    /// external document narrowing), recorded as <c>AnalysisAuthorizationFilter.AuthorizeCreateAsync</c> records it; <c>null</c>
    /// for a request the filter recorded nothing on.</param>
    /// <param name="probe">The request's caller-rights probe (default: the harness's).</param>
    private Task<IResult> RouteCreateAnalysis(Guid document, Guid? filterAllowed = null, CallerRecordAccessProbe? probe = null)
    {
        var analyses = new Mock<IAnalysisDataverseService>(MockBehavior.Strict);
        var http = RouteHttpContext(probe);
        if (filterAllowed is { } allowed)
            Sprk.Bff.Api.Api.Filters.AnalysisAuthorizationFilter.RecordDocumentAttachAuthorized(http, allowed);
        return AnalysisEndpoints.CreateAnalysis(
            new CreateAnalysisRequest("Analysis - NDA.pdf", document, [], [], []),
            Service(), _user, analyses.Object, NullLogger<AnalysisOrchestrationService>.Instance, http,
            CancellationToken.None);
    }

    /// <summary>
    /// An analysis filed under a document the caller can read, with a profile that copies the document's description, and its
    /// name onto the analysis's name (which the request always sends, so it shows fill-only).
    /// </summary>
    private void TheDocumentOfAnAnalysis()
    {
        _world.WithRecord("sprk_document", TypedRouteDocument, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _stamps.Row("sprk_document", TypedRouteDocument);
        _readable.Add(TypedRouteDocument);
        _sources[("sprk_document", TypedRouteDocument)] = new Entity("sprk_document", TypedRouteDocument)
        {
            ["sprk_documentdescription"] = "From the document",
            ["sprk_documentname"] = "NDA.pdf",
        };
        _user.Columns["sprk_analysis"] = new()
        {
            ["sprk_name"] = ("String", true),
            ["sprk_description"] = ("Memo", true),
        };
        Profile("sprk_document", "sprk_analysis",
            Rule("sprk_documentdescription", "sprk_description", Memo),
            Rule("sprk_documentname", "sprk_name", Text));
    }

    /// <summary>The 201 body exactly as the route serializes it (web defaults, as Minimal API writes it).</summary>
    private static string AnalysisBodyOf(IResult result) =>
        JsonSerializer.Serialize(
            result.Should().BeOfType<Created<CreateAnalysisResponse>>().Subject.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>The 201 body with the new row's id taken out, so two creates' bodies can be compared byte for byte.</summary>
    private static string BodyWithoutId(IResult result) =>
        AnalysisBodyOf(result).Replace(((Created<CreateAnalysisResponse>)result).Value!.AnalysisId.ToString("D"), "{id}", StringComparison.Ordinal);

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
    /// Task 043 AC1 (owner 2026-11-04; flips 031's fail-closed pin): the analysis route asks for the mapping of the document it is
    /// anchored to (FR-19, FR-03). The caller's own Read on the document is asked on the Document access path's set
    /// (<c>sprk_documents</c>) BEFORE the document is read app-only (FR-04); the route filter's decision on the body document
    /// stands for the external document narrowing. The profile's value fills the untouched column, the request's own name wins
    /// (fill-only, FR-02), the read is masked to the caller, and the 201 body has no warnings member.
    /// </summary>
    [Fact]
    public async Task TypedAnalysisRoute_ADocumentTheCallerCanRead_MapsItsProfile_FillOnly_ReadAsTheCaller_WithNoWarnings()
    {
        TheDocumentOfAnAnalysis();

        var result = await RouteCreateAnalysis(TypedRouteDocument, filterAllowed: TypedRouteDocument);

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        var (table, _, fields) = _creates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_analysis");
        StringOf(fields, "sprk_description").Should().Be("From the document", "FR-19: the Document to Analysis profile fills the column");
        StringOf(fields, "sprk_name").Should().Be("Analysis - NDA.pdf", "FR-02: the request's own value wins");
        StringOf(fields, "sprk_DocumentId@odata.bind").Should().Be($"/sprk_documents({TypedRouteDocument:D})");
        _probe.Asked.Should().ContainSingle().Which.Should().Be((CallerToken, "sprk_documents", TypedRouteDocument));
        _journal.Should().ContainInOrder("profile", "probe", "source-read", "user:source-read");
        _callerRead.Paths.Should().Equal($"sprk_documents({TypedRouteDocument:D})");
        ((Created<CreateAnalysisResponse>)result).Value!.Warnings.Should().BeNull();
        AnalysisBodyOf(result).Should().NotContain("warnings", "AC6: no warnings, no member");
    }

    public static TheoryData<string> DocumentNotReadable => new() { "cannot-read", "probe-faults" };

    /// <summary>
    /// Task 043 AC2: a document the caller cannot read and a probe that faults give the same create (201, nothing mapped) with
    /// the one uniform warning in a byte-identical body, and the document is never read app-only (the probe answered first). A
    /// document that does not exist never reaches the handler on this route (the filter's Read + AppendTo refuses it, 403, the
    /// authorization contract suite); that a missing document and a denied one get the same answer from the mapping read
    /// itself is <c>MappingSourceReadProbeTests</c>'.
    /// </summary>
    [Theory]
    [MemberData(nameof(DocumentNotReadable))]
    public async Task TypedAnalysisRoute_ADocumentTheCallerCannotRead_MapsNothing_WithTheOneWarning_InTheSameBody(string shape)
    {
        TheDocumentOfAnAnalysis();
        CallerRecordAccessProbe? probe = null;
        if (shape == "cannot-read")
        {
            _readable.Remove(TypedRouteDocument);
            _rights[TypedRouteDocument] = AccessRights.Write | AccessRights.AppendTo | AccessRights.Append;
        }
        else
        {
            probe = new FaultingProbe(_journal);
        }

        var result = await RouteCreateAnalysis(TypedRouteDocument, filterAllowed: TypedRouteDocument, probe: probe);

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        _creates.Should().ContainSingle().Which.Fields.Keys.Should().NotContain("sprk_description");
        StringOf(_creates.Single().Fields, "sprk_name").Should().Be("Analysis - NDA.pdf");
        _journal.Should().Contain("probe").And.NotContain("source-read", "the document is not read app-only before the caller's Read allowed it");
        _sourceReads.Should().BeEmpty();
        BodyWithoutId(result).Should().Be(
            $$"""{"analysisId":"{id}","warnings":["{{MappingSourceReadProbe.SourceUnavailableWarning}}"]}""",
            "AC2/AC6: the same body for each case, naming no record");
    }

    public static TheoryData<string> DocumentNotShown => new() { "hidden", "narrowing-faults", "no-decision" };

    /// <summary>
    /// Task 043 AC3 at the service: for a caller who can read the document, a document the external document narrowing does not
    /// show (hidden, a faulted narrowing, or no decision from the route) maps nothing, with the same one warning as AC2, and is
    /// never read app-only. Where it shows it, AC1's outcome. (Through the real route filter: the authorization contract suite.)
    /// </summary>
    [Theory]
    [MemberData(nameof(DocumentNotShown))]
    public async Task CreateAsync_ADocumentTheNarrowingDoesNotShow_MapsNothing_WithTheOneWarning(string shape)
    {
        TheDocumentOfAnAnalysis();
        Func<Guid, CancellationToken, Task<bool>>? decision = shape switch
        {
            "hidden" => (_, _) => Task.FromResult(false),
            "narrowing-faults" => (_, _) => throw new HttpRequestException("narrowing read failed"),
            _ => null,
        };

        var outcome = await Service().CreateAsync(AnalysisUnder(TypedRouteDocument, decision), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Which.Fields.Keys.Should().NotContain("sprk_description");
        outcome.MappingWarnings.Should().ContainSingle().Which.Should().Be(new RecordCreationWarning(
            RecordCreationWarning.MappingSourceUnavailableCode, null, MappingSourceReadProbe.SourceUnavailableWarning));
        _probe.Asked.Should().ContainSingle().Which.Should().Be((CallerToken, "sprk_documents", TypedRouteDocument));
        _sourceReads.Should().BeEmpty("the document is not read app-only before the narrowing has shown it");
    }

    [Fact]
    public async Task CreateAsync_ADocumentTheNarrowingShows_MapsItsProfile()
    {
        TheDocumentOfAnAnalysis();

        var outcome = await Service().CreateAsync(
            AnalysisUnder(TypedRouteDocument, (id, _) => Task.FromResult(id == TypedRouteDocument)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_description").Should().Be("From the document");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    /// <summary>The analysis route's create body for <paramref name="document"/>, mapped as the caller with
    /// <paramref name="documentShown"/> as the narrowing's answer.</summary>
    private RecordCreationRequest AnalysisUnder(Guid document, Func<Guid, CancellationToken, Task<bool>>? documentShown) =>
        new(AnalysisEndpoints.AnalysisTable, new RecordCreationPayload.WebApi(JsonSerializer.SerializeToElement(
            AnalysisEndpoints.BuildCreatePayload(new CreateAnalysisRequest("Analysis - NDA.pdf", document, [], [], [])))))
        {
            CorrelationId = FixedCorrelation,
            Mapping = RecordCreationMapping.ForCaller(_probe, CallerToken, documentShown: documentShown),
        };

    /// <summary>The production probe whose rights question throws (a RetrievePrincipalAccess fault), journaled like the harness's.</summary>
    private sealed class FaultingProbe(List<string> journal)
        : CallerRecordAccessProbe(new HttpClient(), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            journal.Add("probe");
            throw new HttpRequestException("RetrievePrincipalAccess failed");
        }
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
