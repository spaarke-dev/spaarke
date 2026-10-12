using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDataService;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// Owner decision 2026-10-11 (task 032 follow-up, FR-26), option A: <c>DELETE /api/v1/external/servicerequests/{id}</c>.
/// Only the REQUESTER (<c>sprk_requestedby</c> = the caller's contact) may delete, and only while NOTHING is related to the
/// request (every 1:N relationship from <c>sprk_servicerequest</c>); otherwise 409 <c>has_related_records</c> and the user
/// asks legal to recall it (a message post, task 034). Driven through the REAL route and handler on the task 032 fixture;
/// the relationship probe's transport is tested against a hand-written handler (ADR-038 B1).
/// </summary>
[Trait("category", "external-access")]
public sealed class ExternalDeleteServiceRequestTests : IClassFixture<ExternalDeleteOwnTests.Fixture>
{
    private static readonly Guid Me = Guid.Parse("99999999-9999-9999-9999-999999999132");
    private static readonly Guid SomeoneElse = Guid.Parse("88888888-8888-8888-8888-888888888132");

    private readonly ExternalDeleteOwnTests.Fixture _fixture;

    public ExternalDeleteServiceRequestTests(ExternalDeleteOwnTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    public static TheoryData<string> EveryRelationship()
    {
        var data = new TheoryData<string>();
        foreach (var relation in ServiceRequestRelations)
            data.Add(relation.EntityLogicalName);
        return data;
    }

    private Guid SeedRequest(Guid requester)
    {
        var id = Guid.NewGuid();
        _fixture.Data.Requesters[id] = requester;
        return id;
    }

    // ── 204: the requester, nothing related ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Requester_WithNoRelatedRows_Is204_AndTheRequestIsGone()
    {
        var id = SeedRequest(Me);
        _fixture.Principal = Workforce(Me);

        using var response = await Delete(id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        _fixture.Data.Requesters.ContainsKey(id).Should().BeFalse();
        _fixture.Data.ServiceRequestDeletes.Should().Equal(id);
    }

    // ── 409: option A, any related row of any kind ───────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryRelationship))]
    public async Task OneRelatedRow_OfAnyKind_Is409_HasRelatedRecords_AndNothingIsDeleted(string relatedEntity)
    {
        var id = SeedRequest(Me);
        _fixture.Data.RelatedRows[(relatedEntity, id)] = true;
        _fixture.Principal = Workforce(Me);

        using var response = await Delete(id);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        ProblemCode(body).Should().Be("has_related_records");
        _fixture.Data.Requesters.ContainsKey(id).Should().BeTrue("a request with activity is kept");
        _fixture.Data.ServiceRequestDeletes.Should().BeEmpty("no delete is sent for a request with related rows");
    }

    [Fact]
    public async Task TheConflict_TellsTheUserTheRequestHasActivity_AndTheyCanAskLegalToRecallIt()
    {
        var id = SeedRequest(Me);
        _fixture.Data.RelatedRows[("sprk_document", id)] = true;
        _fixture.Data.RelatedRows[("sprk_communication", id)] = true;
        _fixture.Principal = Workforce(Me);

        using var response = await Delete(id);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var detail = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString();
        detail.Should().Contain("already has activity").And.Contain("can't be deleted").And.Contain("recall");
    }

    // ── 409: a closed request is never deleted (owner decision 2026-10-12) ───────────────────────────────────────

    [Fact]
    public async Task AClosedRequest_Is409_ServiceRequestClosed_AndNoDeleteIsSent()
    {
        var id = SeedRequest(Me);
        _fixture.Data.States[id] = ServiceRequestOpenState.Closed;
        _fixture.Principal = Workforce(Me);

        using var response = await Delete(id);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        ProblemCode(body).Should().Be("service_request_closed");
        _fixture.Data.Requesters.ContainsKey(id).Should().BeTrue("a closed request is kept");
        _fixture.Data.ServiceRequestDeletes.Should().BeEmpty("no delete call is made for a closed request");
    }

    [Fact]
    public async Task AClosedRequest_IsDecidedBeforeTheRelatedRecordsCheck()
    {
        // Closed AND with activity: the closed answer wins, and the relationship probe never runs.
        var id = SeedRequest(Me);
        _fixture.Data.States[id] = ServiceRequestOpenState.Closed;
        _fixture.Data.RelatedRows[("sprk_document", id)] = true;
        _fixture.Principal = Workforce(Me);

        using var response = await Delete(id);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ProblemCode(await response.Content.ReadAsStringAsync()).Should().Be("service_request_closed");
        _fixture.Data.RelationshipProbeCalls.Should().Be(0);
    }

    [Fact]
    public async Task AnOpenRequest_WithActivity_IsStillTheRelatedRecordsConflict()
    {
        var id = SeedRequest(Me);
        _fixture.Data.States[id] = ServiceRequestOpenState.Open;
        _fixture.Data.RelatedRows[("sprk_document", id)] = true;
        _fixture.Principal = Workforce(Me);

        using var response = await Delete(id);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ProblemCode(await response.Content.ReadAsStringAsync()).Should().Be("has_related_records");
    }

    [Fact]
    public async Task AnUnknownState_IsRefused_FailClosed_AndNothingIsDeleted()
    {
        var id = SeedRequest(Me);
        _fixture.Data.States[id] = ServiceRequestOpenState.Unknown;
        _fixture.Principal = Workforce(Me);

        using var response = await Delete(id);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
        ProblemCode(body).Should().Be("sdap.access.delete_own_unavailable");
        _fixture.Data.ServiceRequestDeletes.Should().BeEmpty();
        _fixture.Data.RelationshipProbeCalls.Should().Be(0);
    }

    [Fact]
    public async Task AnUnreadableState_Is503_AndNothingIsDeleted()
    {
        var id = SeedRequest(Me);
        _fixture.Data.StateFault = new DataverseUnavailableException("throttled");
        _fixture.Principal = Workforce(Me);

        using var response = await Delete(id);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _fixture.Data.ServiceRequestDeletes.Should().BeEmpty();
    }

    [Fact]
    public async Task NotTheRequester_WhoseRequestIsClosed_IsStill403_NotAConflict()
    {
        // The requester gate runs first: a closed 409 would tell a stranger the request exists.
        var id = SeedRequest(SomeoneElse);
        _fixture.Data.States[id] = ServiceRequestOpenState.Closed;
        _fixture.Principal = Workforce(Me);

        using var response = await Delete(id);

        await ShouldBeTheUniformDenial(response);
    }

    [Theory]
    [InlineData(0, ServiceRequestOpenState.Open)]
    [InlineData(1, ServiceRequestOpenState.Closed)]
    [InlineData(2, ServiceRequestOpenState.Unknown)]
    [InlineData(-1, ServiceRequestOpenState.Unknown)]
    [InlineData(null, ServiceRequestOpenState.Unknown)]
    public void TheStateRule_OnlyZeroIsOpen_OnlyOneIsClosed_ElseUnknown(int? statecode, ServiceRequestOpenState expected)
    {
        ServiceRequestStateFor(statecode).Should().Be(expected);
        default(ServiceRequestOpenState).Should().Be(ServiceRequestOpenState.Unknown, "a default value must fail closed");
    }

    [Theory]
    [InlineData("{\"statecode\":0}", ServiceRequestOpenState.Open)]
    [InlineData("{\"statecode\":1}", ServiceRequestOpenState.Closed)]
    [InlineData("{\"statecode\":2}", ServiceRequestOpenState.Unknown)]
    [InlineData("{\"statecode\":null}", ServiceRequestOpenState.Unknown)]
    [InlineData("{\"statecode\":\"0\"}", ServiceRequestOpenState.Unknown)]
    [InlineData("{}", ServiceRequestOpenState.Unknown)]
    public async Task TheStateRead_IsOneAppOnlyRead_OfStatecode_AndFailsClosedOnAnythingElse(string body, ServiceRequestOpenState expected)
    {
        var handler = new StateHandler(HttpStatusCode.OK, body);
        var id = Guid.NewGuid();

        (await NewService(handler).GetServiceRequestOpenStateAsync(id)).Should().Be(expected);

        handler.Requests.Should().Equal($"https://org.test/api/data/v9.2/sprk_servicerequests({id})?$select=statecode");
        handler.Authorization.Should().OnlyContain(a => a == "Bearer stub-token");
    }

    [Fact]
    public async Task TheStateRead_OfAnAbsentRequest_IsUnknown()
    {
        var handler = new StateHandler(HttpStatusCode.NotFound, "{}");

        (await NewService(handler).GetServiceRequestOpenStateAsync(Guid.NewGuid())).Should().Be(ServiceRequestOpenState.Unknown);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task TheStateRead_ThrowsWhenDataverseFails(HttpStatusCode status)
    {
        var act = () => NewService(new StateHandler(status, "{}")).GetServiceRequestOpenStateAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<DataverseUnavailableException>();
    }

    // ── 403: only the requester; no existence or activity oracle ─────────────────────────────────────────────────

    [Fact]
    public async Task NotTheRequester_Is403_AndNothingIsDeleted()
    {
        var id = SeedRequest(SomeoneElse);
        _fixture.Principal = Workforce(Me);

        using var response = await Delete(id);

        await ShouldBeTheUniformDenial(response);
        _fixture.Data.Requesters.ContainsKey(id).Should().BeTrue();
        _fixture.Data.ServiceRequestDeletes.Should().BeEmpty();
    }

    [Fact]
    public async Task NotTheRequester_WhenTheRequestHasActivity_IsStill403_NotAConflict()
    {
        // A 409 would tell a stranger that the request exists and has activity.
        var id = SeedRequest(SomeoneElse);
        _fixture.Data.RelatedRows[("sprk_document", id)] = true;
        _fixture.Principal = Workforce(Me);

        using var response = await Delete(id);

        await ShouldBeTheUniformDenial(response);
    }

    [Fact]
    public async Task AbsentRequest_AnswersExactlyAsSomeoneElsesRequest()
    {
        var theirs = SeedRequest(SomeoneElse);
        _fixture.Principal = Workforce(Me);

        using var absent = await Delete(Guid.NewGuid());
        using var notMine = await Delete(theirs);

        absent.StatusCode.Should().Be(notMine.StatusCode).And.Be(HttpStatusCode.Forbidden);
        Problem(await absent.Content.ReadAsStringAsync()).Should().Be(Problem(await notMine.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task AWorkforcePrincipal_WithNoContact_Is403_NoDelete_AndNoRelationshipProbe()
    {
        // ContactId == Guid.Empty: the caller has no contact, so they can be nobody's requester. Even a request whose stored
        // requester is the empty id, with no related rows, must not be deleted.
        var id = SeedRequest(Guid.Empty);
        _fixture.Principal = Workforce(Guid.Empty);

        using var response = await Delete(id);

        await ShouldBeTheUniformDenial(response);
        _fixture.Data.ServiceRequestDeletes.Should().BeEmpty();
        _fixture.Data.Requesters.ContainsKey(id).Should().BeTrue();
        _fixture.Data.RelationshipProbeCalls.Should().Be(0, "a refused caller never reaches the relationship probe");
    }

    [Fact]
    public async Task ACiamPartner_WithThePartnerSwitchOff_Is403_EvenForTheirOwnContact()
    {
        var id = SeedRequest(Me);
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = Me,
            Email = "partner@outside.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        };

        using var response = await Delete(id);

        await ShouldBeTheUniformDenial(response);
        _fixture.Data.Requesters.ContainsKey(id).Should().BeTrue();
    }

    // ── 503: could not tell, so nothing is deleted ───────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryRelationship))]
    public async Task AnUnreadableRelationshipCheck_Is503_AndNothingIsDeleted(string unreadableEntity)
    {
        var id = SeedRequest(Me);
        _fixture.Data.UnreadableRelationship = unreadableEntity;
        _fixture.Principal = Workforce(Me);

        using var response = await Delete(id);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
        ProblemCode(body).Should().Be("sdap.access.delete_own_unavailable");
        _fixture.Data.Requesters.ContainsKey(id).Should().BeTrue();
        _fixture.Data.ServiceRequestDeletes.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnreadableRequester_Is503_NotA403_AndNothingIsDeleted()
    {
        var id = SeedRequest(Me);
        _fixture.Data.RequesterFault = new DataverseUnavailableException("throttled");
        _fixture.Principal = Workforce(Me);

        using var response = await Delete(id);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        ProblemCode(await response.Content.ReadAsStringAsync()).Should().Be("sdap.access.service_request_unreadable");
        _fixture.Data.ServiceRequestDeletes.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedRowDelete_Is503()
    {
        var id = SeedRequest(Me);
        _fixture.Data.DeleteFault = new DataverseUnavailableException("throttled");
        _fixture.Principal = Workforce(Me);

        using var response = await Delete(id);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _fixture.Data.Requesters.ContainsKey(id).Should().BeTrue();
    }

    // ── the real relationship probe (transport level) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheProbe_AsksEveryRelationship_AppOnly_WithNoStateFilter_AndOneRow()
    {
        var handler = new RelationshipHandler();
        var id = Guid.NewGuid();

        (await NewService(handler).HasRelatedRecordsAsync(id)).Should().BeFalse();

        handler.Requests.Should().HaveCount(8);
        foreach (var relation in ServiceRequestRelations)
        {
            var lookup = $"_{relation.LookupAttribute}_value";
            handler.Requests.Should().Contain(
                $"https://org.test/api/data/v9.2/{relation.EntitySet}?$select={lookup}&$filter={lookup} eq {id}&$top=1");
        }
        handler.Requests.Should().OnlyContain(r => !r.Contains("statecode"), "an inactive related row still blocks the delete");
        handler.Authorization.Should().OnlyContain(a => a == "Bearer stub-token", "the check runs as the app, never as the caller");
    }

    [Theory]
    [MemberData(nameof(EveryRelationship))]
    public async Task TheProbe_FindsARowInAnyOneRelationship(string entity)
    {
        var set = ServiceRequestRelations.Single(r => r.EntityLogicalName == entity).EntitySet;
        var handler = new RelationshipHandler { RowsIn = { set } };

        (await NewService(handler).HasRelatedRecordsAsync(Guid.NewGuid())).Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task TheProbe_ThrowsWhenAnyRelationshipIsUnreadable_EvenIfAnotherFoundARow(HttpStatusCode failure)
    {
        var handler = new RelationshipHandler { RowsIn = { "sprk_documents" }, FailIn = { "sprk_memos" }, FailureStatus = failure };

        var act = () => NewService(handler).HasRelatedRecordsAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<DataverseUnavailableException>(
            "could not tell is never reported as 'none' (nor as 'some', which would hide the fault)");
    }

    [Fact]
    public async Task TheProbe_ThrowsOnA200WithNoValueArray_RatherThanReadingItAsNoRows()
    {
        var handler = new RelationshipHandler { BodyWithoutValueIn = { "sprk_todos" } };

        var act = () => NewService(handler).HasRelatedRecordsAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<DataverseUnavailableException>();
    }

    [Fact]
    public async Task TheProbe_ThrowsOnATransportFault()
    {
        var handler = new RelationshipHandler { ThrowIn = { "sprk_signals" } };

        var act = () => NewService(handler).HasRelatedRecordsAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<DataverseUnavailableException>();
    }

    [Fact]
    public async Task TheRowDelete_IsOneAppOnlyDelete_OfTheRequest()
    {
        var handler = new RelationshipHandler();
        var id = Guid.NewGuid();

        (await NewService(handler).DeleteRecordAsync(DeletableKind.ServiceRequest, id)).Should().BeTrue();

        handler.Deletes.Should().Equal($"https://org.test/api/data/v9.2/sprk_servicerequests({id})");
    }

    [Fact]
    public async Task TheCreatorStampRead_IsNotUsedForARequest()
    {
        var act = () => NewService(new RelationshipHandler()).GetDeleteTargetAsync(DeletableKind.ServiceRequest, Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TheRelationshipList_IsExactlyTheSolutionsOneToManyRelationships()
    {
        // The solution source is the authority: a relationship added there must be added to ServiceRequestRelations, or a
        // delete would orphan its rows without the check noticing. Read from the repo, not from a copy of the list, and from
        // EVERY solution file (a relationship is exported under the referencing entity, or the referenced one, or as
        // ManyToMany), not just sprk_ServiceRequest.xml.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "dataverse", "solutions")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the solution source is part of the repository checkout");

        var documents = new List<System.Xml.Linq.XDocument>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src", "dataverse", "solutions"), "*.xml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            // Cheap pre-filter: only a file that has relationships AND names the entity can matter.
            if (!text.Contains("<EntityRelationship ", StringComparison.Ordinal)
                || !text.Contains("sprk_servicerequest", StringComparison.OrdinalIgnoreCase))
                continue;
            documents.Add(System.Xml.Linq.XDocument.Parse(text));
        }
        documents.Should().NotBeEmpty("sprk_ServiceRequest.xml itself names the entity");

        var found = FindServiceRequestParentRelationships(documents);

        found.UnsupportedRelationships.Should().BeEmpty(
            "ServiceRequestRelations models 1:N lookups only; a ManyToMany naming sprk_servicerequest needs its own check before delete");
        found.OneToMany.OrderBy(x => x).Should().Equal(ServiceRequestRelations
            .Select(r => r.EntityLogicalName + "." + r.LookupAttribute)
            .OrderBy(x => x));
        found.OneToMany.Should().HaveCount(8);
    }

    // Must-fire / must-not-fire controls for the scan above, on injected documents (a relationship exported under the
    // referencing entity's file, a ManyToMany, and the request as the CHILD of another entity).

    private static System.Xml.Linq.XDocument Doc(string relationships)
        => System.Xml.Linq.XDocument.Parse($"<ImportExportXml><EntityRelationships>{relationships}</EntityRelationships></ImportExportXml>");

    private const string OneToManyFromRequest = @"<EntityRelationship Name=""sprk_widget_RegardingServiceRequest"">
        <EntityRelationshipType>OneToMany</EntityRelationshipType>
        <ReferencingEntityName>sprk_Widget</ReferencingEntityName>
        <ReferencedEntityName>sprk_ServiceRequest</ReferencedEntityName>
        <ReferencingAttributeName>sprk_RegardingServiceRequest</ReferencingAttributeName>
        <CascadeDelete>RemoveLink</CascadeDelete></EntityRelationship>";

    [Fact]
    public void TheScan_ReportsANewOneToMany_ExportedUnderTheReferencingEntitysFile()
    {
        var found = FindServiceRequestParentRelationships(new[] { Doc(OneToManyFromRequest) });

        found.OneToMany.Should().Equal("sprk_widget.sprk_regardingservicerequest");
        found.OneToMany.Except(ServiceRequestRelations.Select(r => r.EntityLogicalName + "." + r.LookupAttribute))
            .Should().NotBeEmpty("a relationship missing from ServiceRequestRelations must make the pin test fail");
    }

    [Fact]
    public void TheScan_ReportsAManyToMany_NamingTheRequestOnEitherSide()
    {
        const string first = @"<EntityRelationship Name=""a_nn""><EntityRelationshipType>ManyToMany</EntityRelationshipType>
            <FirstEntityName>sprk_ServiceRequest</FirstEntityName><SecondEntityName>sprk_Widget</SecondEntityName>
            <IntersectEntityName>sprk_a_nn</IntersectEntityName></EntityRelationship>";
        const string second = @"<EntityRelationship Name=""b_nn""><EntityRelationshipType>ManyToMany</EntityRelationshipType>
            <FirstEntityName>sprk_Widget</FirstEntityName><SecondEntityName>sprk_servicerequest</SecondEntityName>
            <IntersectEntityName>sprk_b_nn</IntersectEntityName></EntityRelationship>";

        var found = FindServiceRequestParentRelationships(new[] { Doc(first + second) });

        found.OneToMany.Should().BeEmpty();
        found.UnsupportedRelationships.Should().BeEquivalentTo("a_nn", "b_nn");
    }

    [Fact]
    public void TheScan_IgnoresRelationships_WhereTheRequestIsTheChild()
    {
        // sprk_servicerequest -> contact (sprk_requestedby): the request holds the lookup, so deleting it orphans nothing.
        const string requestAsChild = @"<EntityRelationship Name=""sprk_servicerequest_RequestedBy_contact"">
            <EntityRelationshipType>OneToMany</EntityRelationshipType>
            <ReferencingEntityName>sprk_ServiceRequest</ReferencingEntityName>
            <ReferencedEntityName>Contact</ReferencedEntityName>
            <ReferencingAttributeName>sprk_RequestedBy</ReferencingAttributeName></EntityRelationship>";
        const string unrelated = @"<EntityRelationship Name=""x""><EntityRelationshipType>OneToMany</EntityRelationshipType>
            <ReferencingEntityName>sprk_A</ReferencingEntityName><ReferencedEntityName>sprk_B</ReferencedEntityName>
            <ReferencingAttributeName>sprk_b</ReferencingAttributeName></EntityRelationship>";

        var found = FindServiceRequestParentRelationships(new[] { Doc(requestAsChild + unrelated) });

        found.OneToMany.Should().BeEmpty();
        found.UnsupportedRelationships.Should().BeEmpty();
    }

    /// <summary>Every relationship in the documents that makes <c>sprk_servicerequest</c> a PARENT: a 1:N where it is the
    /// referenced entity (as <c>referencingEntity.lookup</c>, lower case), and any ManyToMany that names it.</summary>
    private static (List<string> OneToMany, List<string> UnsupportedRelationships) FindServiceRequestParentRelationships(
        IEnumerable<System.Xml.Linq.XDocument> documents)
    {
        const string request = "sprk_servicerequest";
        var oneToMany = new List<string>();
        var unsupported = new List<string>();
        foreach (var relationship in documents.SelectMany(d => d.Descendants("EntityRelationship")))
        {
            string? Text(string name) => (string?)relationship.Element(name);
            bool IsRequest(string? v) => string.Equals(v?.Trim(), request, StringComparison.OrdinalIgnoreCase);

            if (string.Equals(Text("EntityRelationshipType"), "ManyToMany", StringComparison.OrdinalIgnoreCase))
            {
                if (IsRequest(Text("FirstEntityName")) || IsRequest(Text("SecondEntityName")))
                    unsupported.Add((string?)relationship.Attribute("Name") ?? "(unnamed)");
            }
            else if (IsRequest(Text("ReferencedEntityName")))
            {
                oneToMany.Add(Text("ReferencingEntityName")!.Trim().ToLowerInvariant()
                              + "." + Text("ReferencingAttributeName")!.Trim().ToLowerInvariant());
            }
        }

        return (oneToMany, unsupported);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> Delete(Guid id)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.DeleteAsync($"/api/v1/external/servicerequests/{id}");
    }

    private static CallerPrincipal Workforce(Guid contact) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = contact,
        Email = "staff@customer.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    private static async Task ShouldBeTheUniformDenial(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
        ProblemCode(body).Should().Be("sdap.access.deny.delete_own");
    }

    /// <summary>The code of a problem body: one key, <c>reasonCode</c>, on every delete route.</summary>
    private static string? ProblemCode(string body)
    {
        var root = JsonDocument.Parse(body).RootElement;
        root.TryGetProperty("code", out _).Should().BeFalse();
        root.TryGetProperty("errorCode", out _).Should().BeFalse();
        return root.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
    }

    private static string Problem(string body) =>
        string.Join("|", JsonDocument.Parse(body).RootElement.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "instance" or "correlationId"))
            .Select(p => $"{p.Name}={p.Value}"));

    private static ExternalDataService NewService(HttpMessageHandler transport) => new(
        new HttpClient(transport),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
            .Build(),
        new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential(),
        NullLogger<ExternalDataService>.Instance);

    /// <summary>One service-request row read (<c>$select=statecode</c>) with a scripted status and body.</summary>
    private sealed class StateHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        public List<string?> Authorization { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests)
            {
                Requests.Add(Uri.UnescapeDataString(request.RequestUri!.ToString()));
                Authorization.Add(request.Headers.Authorization?.ToString());
            }
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// Dataverse's collection endpoints as the relationship probe uses them. By default every entity set answers an empty
    /// page; <see cref="RowsIn"/>, <see cref="FailIn"/>, <see cref="BodyWithoutValueIn"/> and <see cref="ThrowIn"/> script
    /// individual sets.
    /// </summary>
    private sealed class RelationshipHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        public List<string> Deletes { get; } = new();
        public List<string?> Authorization { get; } = new();
        public HashSet<string> RowsIn { get; } = new();
        public HashSet<string> FailIn { get; } = new();
        public HashSet<string> BodyWithoutValueIn { get; } = new();
        public HashSet<string> ThrowIn { get; } = new();
        public HttpStatusCode FailureStatus { get; set; } = HttpStatusCode.InternalServerError;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (request.Method == HttpMethod.Delete)
            {
                lock (Deletes) Deletes.Add(url);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            lock (Requests)
            {
                Requests.Add(Uri.UnescapeDataString(url));
                Authorization.Add(request.Headers.Authorization?.ToString());
            }

            var set = request.RequestUri.AbsolutePath.Split('/').Last();
            if (ThrowIn.Contains(set))
                return Task.FromException<HttpResponseMessage>(new HttpRequestException("connection reset"));
            if (FailIn.Contains(set))
                return Task.FromResult(new HttpResponseMessage(FailureStatus));

            var json = BodyWithoutValueIn.Contains(set)
                ? "{}"
                : RowsIn.Contains(set)
                    ? "{\"value\":[{\"sprk_x\":\"1\"}]}"
                    : "{\"value\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
