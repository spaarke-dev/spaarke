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
        // delete would orphan its rows without the check noticing. Read from the repo, not from a copy of the list.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "dataverse", "solutions", "SpaarkeMaster",
                   "Other", "Relationships", "sprk_ServiceRequest.xml")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the solution source is part of the repository checkout");

        var xml = System.Xml.Linq.XDocument.Load(Path.Combine(dir!.FullName, "src", "dataverse", "solutions", "SpaarkeMaster",
            "Other", "Relationships", "sprk_ServiceRequest.xml"));
        var fromSolution = xml.Descendants("EntityRelationship")
            .Where(r => string.Equals((string?)r.Element("ReferencedEntityName"), "sprk_ServiceRequest", StringComparison.OrdinalIgnoreCase))
            .Select(r => ((string)r.Element("ReferencingEntityName")!).ToLowerInvariant()
                         + "." + ((string)r.Element("ReferencingAttributeName")!).ToLowerInvariant())
            .OrderBy(x => x)
            .ToList();

        fromSolution.Should().Equal(ServiceRequestRelations
            .Select(r => r.EntityLogicalName + "." + r.LookupAttribute)
            .OrderBy(x => x));
        fromSolution.Should().HaveCount(8);
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
