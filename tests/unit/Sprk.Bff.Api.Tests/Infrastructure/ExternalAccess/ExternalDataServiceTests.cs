using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// Unit tests for <see cref="ExternalDataService"/> covering the public surface introduced by
/// smart-todo-decoupling-r3 task 007 (FR-29). The service interacts with Dataverse via
/// HttpClient + managed-identity tokens, so end-to-end query/write semantics are exercised
/// by integration tests; here we verify the static helpers + the ADR-024 resolver-URL contract.
///
/// Product portability: <see cref="ExternalDataService.BuildRecordUrl"/> MUST return a
/// RELATIVE URL — no org URL or tenant id may be hard-coded.
/// </summary>
[Trait("status", "repaired")]
public class ExternalDataServiceTests
{
    // =========================================================================
    // BuildRecordUrl (ADR-024 resolver field — sprk_regardingrecordurl)
    //
    // Mirrors the contract enforced by TodoRegardingBuilder.BuildRecordUrl. The URL is
    // RELATIVE so it works across environments (dev, staging, prod) without rebaking
    // the BFF or DTO contract per environment.
    // =========================================================================

    [Fact]
    public void BuildRecordUrl_IsRelative_NoOrgUrlHardcoded()
    {
        // Arrange
        var entityName = "sprk_project";
        var recordId = "11111111-1111-1111-1111-111111111111";

        // Act
        var url = ExternalDataService.BuildRecordUrl(entityName, recordId);

        // Assert
        url.Should().StartWith("/main.aspx",
            "the record URL must be relative — host origin is resolved at click time per ADR-024 portability");
        url.Should().NotStartWith("http://",
            "no absolute org URL allowed (product portability)");
        url.Should().NotStartWith("https://",
            "no absolute org URL allowed (product portability)");
        url.Should().NotContain(".dynamics.com",
            "no Dataverse org host may be embedded in DTO contracts");
        url.Should().NotContain(".crm.",
            "no Dataverse org host may be embedded in DTO contracts");
    }

    [Fact]
    public void BuildRecordUrl_EncodesEntityAndIdInQuery()
    {
        var entityName = "sprk_project";
        var recordId = "22222222-2222-2222-2222-222222222222";

        var url = ExternalDataService.BuildRecordUrl(entityName, recordId);

        url.Should().Contain("etn=sprk_project");
        url.Should().Contain($"id={recordId}");
        url.Should().Contain("pagetype=entityrecord");
    }

    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_event")]
    [InlineData("sprk_invoice")]
    [InlineData("contact")]
    public void BuildRecordUrl_WorksAcrossAllRegardingTargetEntities(string entityName)
    {
        // The resolver URL pattern is uniform across all 11 sprk_todo regarding targets
        // (per entity-schema.md). The External Access surface only writes the project
        // regarding here, but the helper is entity-agnostic for symmetry with
        // TodoRegardingBuilder.
        var recordId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var url = ExternalDataService.BuildRecordUrl(entityName, recordId);

        url.Should().Contain($"etn={entityName}");
        url.Should().Contain($"id={recordId}");
        url.Should().StartWith("/main.aspx");
    }

    // =========================================================================
    // FR-25 creator stamp (spaarke-SPA-external-access-platform-r3 task 030)
    //
    // Every create on /api/v1/external/** is app-only, so Dataverse createdby is the BFF application user. The stamp
    // names the RESOLVED principal: a systemuser -> sprk_createdbyperson, a contact -> sprk_createdbycontact. The
    // delete-own check (FR-26, task 032) reads it, so a stamp a client could choose would be an authorization hole.
    // =========================================================================

    private static readonly Guid SystemUserA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ContactA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid OtherSystemUser = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid OtherContact = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    public enum PrincipalKind { SystemUser, SystemUserWithLinkedContact, WorkforceContact, CiamContact }

    private static CallerPrincipal Principal(PrincipalKind kind) => kind switch
    {
        PrincipalKind.SystemUser => new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce, SystemUserId = SystemUserA, ContactId = Guid.Empty,
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        },
        PrincipalKind.SystemUserWithLinkedContact => new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce, SystemUserId = SystemUserA, ContactId = ContactA,
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        },
        PrincipalKind.WorkforceContact => new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce, ContactId = ContactA, ProjectAccess = Array.Empty<CallerProjectAccess>(),
        },
        PrincipalKind.CiamContact => new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact, ContactId = ContactA, ProjectAccess = Array.Empty<CallerProjectAccess>(),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static CallerPrincipal NeitherKind() => new()
    {
        Plane = CallerPrincipalPlane.Workforce, ContactId = Guid.Empty, ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    private static bool ExpectsPerson(PrincipalKind kind) =>
        kind is PrincipalKind.SystemUser or PrincipalKind.SystemUserWithLinkedContact;

    /// <summary>Asserts the payload carries exactly the caller's stamp and no other creator column.</summary>
    private static void AssertStampedAs(IReadOnlyDictionary<string, string?> body, PrincipalKind kind)
    {
        var creatorKeys = body.Keys.Where(ExternalDataService.NamesCreatorColumn).ToList();
        if (ExpectsPerson(kind))
        {
            creatorKeys.Should().Equal(new[] { ExternalDataService.CreatedByPersonBindKey },
                "a systemuser principal stamps sprk_createdbyperson and nothing else");
            body[ExternalDataService.CreatedByPersonBindKey].Should().Be($"/systemusers({SystemUserA:D})");
        }
        else
        {
            creatorKeys.Should().Equal(new[] { ExternalDataService.CreatedByContactBindKey },
                "a contact principal stamps sprk_createdbycontact and nothing else");
            body[ExternalDataService.CreatedByContactBindKey].Should().Be($"/contacts({ContactA:D})");
        }
    }

    private static IReadOnlyDictionary<string, string?> AsStrings(IDictionary<string, object?> body) =>
        body.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString());

    [Theory]
    [InlineData(PrincipalKind.SystemUser)]
    [InlineData(PrincipalKind.SystemUserWithLinkedContact)]
    [InlineData(PrincipalKind.WorkforceContact)]
    [InlineData(PrincipalKind.CiamContact)]
    public void StampCreator_WritesExactlyOneStamp_ForThePrincipalKind(PrincipalKind kind)
    {
        // A workforce systemuser that also carries a linked contact is still a SYSTEMUSER (CallerPrincipal.IsContactPrincipal):
        // the kind decides, never the presence of a contact id, and never the plane.
        var body = new Dictionary<string, object?> { ["sprk_name"] = "x" };

        ExternalDataService.StampCreator(body, Principal(kind));

        AssertStampedAs(AsStrings(body), kind);
        body["sprk_name"].Should().Be("x");
    }

    [Theory]
    [InlineData(PrincipalKind.SystemUser)]
    [InlineData(PrincipalKind.CiamContact)]
    public void StampCreator_RemovesEveryClientSuppliedCreator_AndWritesTheCallers(PrincipalKind kind)
    {
        var body = new Dictionary<string, object?>
        {
            ["sprk_name"] = "x",
            ["sprk_CreatedByPerson@odata.bind"] = $"/systemusers({OtherSystemUser:D})",
            ["sprk_createdbyperson"] = OtherSystemUser.ToString(),
            ["sprk_CreatedByContact@odata.bind"] = $"/contacts({OtherContact:D})",
            ["sprk_createdbycontact"] = OtherContact.ToString(),
            ["_sprk_createdbycontact_value"] = OtherContact.ToString(),
            [" SPRK_CREATEDBYCONTACT@odata.bind "] = $"/contacts({OtherContact:D})",
            ["createdby@odata.bind"] = $"/systemusers({OtherSystemUser:D})",
            ["sprk_requestedby@odata.bind"] = $"/contacts({OtherContact:D})",
        };

        ExternalDataService.StampCreator(body, Principal(kind));

        var strings = AsStrings(body);
        AssertStampedAs(strings, kind);
        strings.Values.Should().NotContain(v => v != null
            && (v.Contains(OtherSystemUser.ToString("D")) || v.Contains(OtherContact.ToString("D"))),
            "no creator value the client supplied may reach Dataverse");
    }

    [Fact]
    public void StampCreator_PrincipalOfNeitherKind_ThrowsAndLeavesThePayloadUntouched()
    {
        var body = new Dictionary<string, object?> { ["sprk_createdbycontact"] = OtherContact.ToString() };

        var act = () => ExternalDataService.StampCreator(body, NeitherKind());

        act.Should().Throw<InvalidOperationException>().WithMessage("*unattributed*");
        body.Should().ContainKey("sprk_createdbycontact", "a refusal decides before it mutates");
    }

    [Fact]
    public void StampCreator_EmptySystemUserId_Throws()
    {
        var emptyUser = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce, SystemUserId = Guid.Empty, ContactId = ContactA,
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        };

        var act = () => ExternalDataService.StampCreator(new Dictionary<string, object?>(), emptyUser);

        act.Should().Throw<InvalidOperationException>(
            "an empty systemuser id is not a person; it must not silently fall through to the contact stamp");
    }

    [Theory]
    [InlineData("sprk_createdbyperson", true)]
    [InlineData("sprk_CreatedByPerson@odata.bind", true)]
    [InlineData("_sprk_createdbyperson_value", true)]
    [InlineData("sprk_createdbycontact", true)]
    [InlineData("sprk_CreatedByContact@odata.bind", true)]
    [InlineData("createdby", true)]
    [InlineData("CreatedBy@odata.bind", true)]
    [InlineData("createdonbehalfby@odata.bind", true)]
    [InlineData("sprk_requestedby@odata.bind", true)]
    [InlineData("sprk_name", false)]
    [InlineData("ownerid@odata.bind", false)]
    [InlineData("sprk_AssignedTo@odata.bind", false)]
    [InlineData("sprk_createdbypersonal", false)]
    [InlineData("createdon", false)]
    [InlineData("", false)]
    public void NamesCreatorColumn_MatchesCreatorColumnsOnly(string key, bool expected) =>
        ExternalDataService.NamesCreatorColumn(key).Should().Be(expected);

    // ── Per create path: the REAL seam, driven through a capturing transport, so the stamp is proved on the body
    //    that is actually POSTed. CreateTodoAsync / CreateEventAsync / CreateDocumentAsync are substitution seams, so a
    //    stamp applied inside one is invisible to every endpoint test that overrides it. ─────────────────────────────

    public enum CreatePath { Todo, Event, Document }

    [Theory]
    [InlineData(CreatePath.Todo, PrincipalKind.SystemUser)]
    [InlineData(CreatePath.Todo, PrincipalKind.CiamContact)]
    [InlineData(CreatePath.Todo, PrincipalKind.WorkforceContact)]
    [InlineData(CreatePath.Event, PrincipalKind.SystemUser)]
    [InlineData(CreatePath.Event, PrincipalKind.CiamContact)]
    [InlineData(CreatePath.Event, PrincipalKind.WorkforceContact)]
    [InlineData(CreatePath.Document, PrincipalKind.SystemUser)]
    [InlineData(CreatePath.Document, PrincipalKind.CiamContact)]
    [InlineData(CreatePath.Document, PrincipalKind.WorkforceContact)]
    public async Task EveryExternalCreatePath_PostsTheCallersStamp(CreatePath path, PrincipalKind kind)
    {
        var (service, handler) = NewService();

        await CreateAsync(service, path, Principal(kind), "{\"sprk_name\":\"Created\"}");

        AssertStampedAs(handler.SinglePostBody(), kind);
    }

    [Theory]
    [InlineData(CreatePath.Todo, PrincipalKind.SystemUser)]
    [InlineData(CreatePath.Todo, PrincipalKind.CiamContact)]
    [InlineData(CreatePath.Event, PrincipalKind.SystemUser)]
    [InlineData(CreatePath.Event, PrincipalKind.CiamContact)]
    public async Task ARequestBodyNamingAnotherCreator_IsStoredWithTheCallersStamp(CreatePath path, PrincipalKind kind)
    {
        // The SPA body names someone else as creator, in every form a client could try. The request is bound the way the
        // minimal API binds it (web JSON defaults) and the real seam posts it.
        var json = $$"""
        {
          "sprk_name": "Created",
          "sprk_createdbyperson": "{{OtherSystemUser:D}}",
          "sprk_CreatedByPerson@odata.bind": "/systemusers({{OtherSystemUser:D}})",
          "sprk_createdbycontact": "{{OtherContact:D}}",
          "sprk_CreatedByContact@odata.bind": "/contacts({{OtherContact:D}})",
          "createdby@odata.bind": "/systemusers({{OtherSystemUser:D}})",
          "sprk_requestedby@odata.bind": "/contacts({{OtherContact:D}})"
        }
        """;
        var (service, handler) = NewService();

        await CreateAsync(service, path, Principal(kind), json);

        AssertStampedAs(handler.SinglePostBody(), kind);
        handler.PostBodies.Single().Should().NotContain(OtherSystemUser.ToString("D"))
            .And.NotContain(OtherContact.ToString("D"), "a client-supplied creator never lands");
    }

    [Theory]
    [InlineData(CreatePath.Todo)]
    [InlineData(CreatePath.Event)]
    [InlineData(CreatePath.Document)]
    public async Task EveryExternalCreatePath_WithAnUnattributablePrincipal_PostsNothing(CreatePath path)
    {
        var (service, handler) = NewService();

        var act = () => CreateAsync(service, path, NeitherKind(), "{\"sprk_name\":\"Created\"}");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unattributed*");
        handler.PostBodies.Should().BeEmpty("no row is written without a creator stamp");
    }

    private static readonly Guid RootId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid TeamId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

    private static Task CreateAsync(ExternalDataService service, CreatePath path, CallerPrincipal creator, string requestJson)
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return path switch
        {
            CreatePath.Todo => service.CreateTodoAsync(
                ExternalDataService.TodoRootKind.Project, RootId,
                JsonSerializer.Deserialize<CreateExternalTodoRequest>(requestJson, web)!, TeamId,
                creator.ContactId == Guid.Empty ? null : creator.ContactId, creator),
            CreatePath.Event => service.CreateEventAsync(
                RootId, JsonSerializer.Deserialize<CreateExternalEventRequest>(requestJson, web)!, TeamId, creator),
            // The upload route's body is the file only; the pointers are built server-side from the SPE upload.
            CreatePath.Document => service.CreateDocumentAsync(
                RootId, new ExternalUploadedFilePointers("drive-1", "item-1", "a.pdf", 10, null), TeamId, creator),
            _ => throw new ArgumentOutOfRangeException(nameof(path)),
        };
    }

    private static (ExternalDataService Service, CapturingHandler Handler) NewService()
    {
        var handler = new CapturingHandler();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
            .Build();
        var service = new ExternalDataService(
            new HttpClient(handler), config, new StubCredential(), NullLogger<ExternalDataService>.Instance);
        return (service, handler);
    }

    /// <summary>Answers every GET with an empty collection and every POST with a created row; records each POST body.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> PostBodies { get; } = new();

        public IReadOnlyDictionary<string, string?> SinglePostBody()
        {
            PostBodies.Should().ContainSingle("exactly one create is posted");
            using var doc = JsonDocument.Parse(PostBodies[0]);
            return doc.RootElement.EnumerateObject().ToDictionary(
                p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText());
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                PostBodies.Add(await request.Content!.ReadAsStringAsync(ct));
                var id = Guid.NewGuid().ToString("D");
                return Json($"{{\"sprk_todoid\":\"{id}\",\"sprk_eventid\":\"{id}\",\"sprk_documentid\":\"{id}\"}}");
            }

            return Json("{\"value\":[]}");
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class StubCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(new AccessToken("test-token", DateTimeOffset.MaxValue));
    }
}
