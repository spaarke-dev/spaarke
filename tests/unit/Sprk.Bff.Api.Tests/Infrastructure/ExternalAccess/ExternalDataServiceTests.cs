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

    [Theory]
    [InlineData(PrincipalKind.SystemUser, true)]
    [InlineData(PrincipalKind.SystemUserWithLinkedContact, true)]
    [InlineData(PrincipalKind.WorkforceContact, true)]
    [InlineData(PrincipalKind.CiamContact, true)]
    public void CanStampCreator_IsTrueForEveryAttributableKind(PrincipalKind kind, bool expected) =>
        ExternalDataService.CanStampCreator(Principal(kind)).Should().Be(expected);

    [Fact]
    public void CanStampCreator_IsFalseForNeitherKind_AndForAnEmptySystemUserId()
    {
        ExternalDataService.CanStampCreator(NeitherKind()).Should().BeFalse();
        ExternalDataService.CanStampCreator(new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce, SystemUserId = Guid.Empty, ContactId = ContactA,
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        }).Should().BeFalse("an empty systemuser id is not a person");
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
    [InlineData(CreatePath.Document, PrincipalKind.SystemUser)]
    [InlineData(CreatePath.Document, PrincipalKind.CiamContact)]
    public void ACreatorKeyInjectedIntoTheBuiltPayload_IsStrippedByStampCreator_AndTheCallersStampWins(
        CreatePath path, PrincipalKind kind)
    {
        // The strip itself: take the payload the REAL builder produces, add creator keys as if a DTO or builder ever passed
        // client fields through, and stamp it exactly as the seam does (builder -> StampCreator -> serialize).
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var body = path switch
        {
            CreatePath.Todo => ExternalDataService.BuildTodoCreatePayload(
                JsonSerializer.Deserialize<CreateExternalTodoRequest>("{\"sprk_name\":\"Created\"}", web)!,
                ExternalDataService.TryGetRootBinding(ExternalDataService.TodoRootKind.Project)!, RootId, "Project", null,
                TeamId, null),
            CreatePath.Event => ExternalDataService.BuildEventCreatePayload(
                RootId, JsonSerializer.Deserialize<CreateExternalEventRequest>("{\"sprk_name\":\"Created\"}", web)!, TeamId),
            CreatePath.Document => ExternalDataService.BuildDocumentCreatePayload(
                ExternalDataService.DocumentParentKind.Project, RootId,new ExternalUploadedFilePointers("drive-1", "item-1", "a.pdf", 10, null), TeamId),
            _ => throw new ArgumentOutOfRangeException(nameof(path)),
        };
        body["sprk_CreatedByContact@odata.bind"] = $"/contacts({OtherContact:D})";
        body["sprk_CreatedByPerson@odata.bind"] = $"/systemusers({OtherSystemUser:D})";
        body["sprk_requestedby@odata.bind"] = $"/contacts({OtherContact:D})";

        ExternalDataService.StampCreator(body, Principal(kind));

        var strings = AsStrings(body);
        AssertStampedAs(strings, kind);
        strings.Values.Should().NotContain(v => v != null
            && (v.Contains(OtherSystemUser.ToString("D")) || v.Contains(OtherContact.ToString("D"))),
            "a creator key present in the built payload never reaches Dataverse");
    }

    [Theory]
    [InlineData(CreatePath.Todo, PrincipalKind.SystemUser)]
    [InlineData(CreatePath.Todo, PrincipalKind.CiamContact)]
    [InlineData(CreatePath.Event, PrincipalKind.SystemUser)]
    [InlineData(CreatePath.Event, PrincipalKind.CiamContact)]
    public async Task ARequestBodyNamingAnotherCreator_IsDroppedByTheClosedDto_AndTheCallersStampIsStored(CreatePath path, PrincipalKind kind)
    {
        // Pins DTO CLOSURE (the closed request DTO has no creator property, so the keys never bind), not the strip: the
        // strip is pinned by the next test, which injects the keys AFTER the builder. The request is bound the way the
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
        handler.Requests.Should().BeEmpty(
            "the stamp decision precedes every lookup, token and request: an unattributable caller costs no round trip");
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
                ExternalDataService.DocumentParentKind.Project, RootId,new ExternalUploadedFilePointers("drive-1", "item-1", "a.pdf", 10, null), TeamId, creator),
            _ => throw new ArgumentOutOfRangeException(nameof(path)),
        };
    }

    // =========================================================================
    // Task 037 review F3-b — the three notification-feed reads, at the HTTP seam. Each asserts the QUERY it sends and the
    // PARSE of what comes back; a renamed column or a dropped predicate fails here, not only in a live tenant.
    // =========================================================================

    private static readonly Guid FeedContact = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private static HttpResponseMessage JsonOk(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage ServerError() => new(HttpStatusCode.InternalServerError);

    private static string Decoded(string url) => Uri.UnescapeDataString(url);

    private static string GrantRow(Guid id, Guid? project, Guid? matter, Guid? workAssignment, string? createdOn, string? grantedDate)
    {
        var parts = new List<string> { $"\"sprk_externalrecordaccessid\":\"{id}\"" };
        parts.Add($"\"_sprk_project_value\":{(project is null ? "null" : $"\"{project}\"")}");
        parts.Add($"\"_sprk_matter_value\":{(matter is null ? "null" : $"\"{matter}\"")}");
        parts.Add($"\"_sprk_workassignment_value\":{(workAssignment is null ? "null" : $"\"{workAssignment}\"")}");
        if (createdOn is not null) parts.Add($"\"createdon\":\"{createdOn}\"");
        if (grantedDate is not null) parts.Add($"\"sprk_granteddate\":\"{grantedDate}\"");
        return "{" + string.Join(",", parts) + "}";
    }

    [Fact]
    public async Task GetRecentGrants_SendsOneQuery_FilteredOnContactStateGrantedDateAndExpiry_Unpaged_NewestFirst()
    {
        var (service, handler) = NewService();

        var read = await service.GetRecentGrantsAsync(FeedContact, new DateOnly(2026, 9, 12), new DateOnly(2026, 10, 12));

        read.Truncated.Should().BeFalse();
        var url = handler.GetUrls.Should().ContainSingle().Subject;
        var decoded = Decoded(url);
        url.Should().Contain("/sprk_externalrecordaccesses?");
        decoded.Should().Contain($"_sprk_contact_value eq {FeedContact}");
        decoded.Should().Contain("statecode eq 0");
        decoded.Should().Contain("sprk_granteddate ge 2026-09-12");
        decoded.Should().Contain("sprk_expiresdate ge 2026-10-12", "a lapsed grant is never announced (the enforcement paths' predicate)");
        decoded.Should().Contain("$orderby=createdon desc,sprk_externalrecordaccessid desc");
        decoded.Should().Contain("createdon").And.Contain("_sprk_project_value").And.Contain("_sprk_matter_value")
            .And.Contain("_sprk_workassignment_value");
        url.Should().NotContain("$top", "$top suppresses @odata.nextLink; the read is paged and never silently cut");
    }

    [Fact]
    public async Task GetRecentGrants_WithModifiedSince_AlsoReturnsRowsModifiedInTheWindow_AndStillRequiresActiveUnexpired()
    {
        var (service, handler) = NewService();

        await service.GetRecentGrantsAsync(
            FeedContact, new DateOnly(2026, 9, 12), new DateOnly(2026, 10, 12),
            modifiedSince: new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));

        var decoded = Decoded(handler.GetUrls.Should().ContainSingle().Subject);
        decoded.Should().Contain("(sprk_granteddate ge 2026-09-12 or modifiedon ge 2026-09-12T12:00:00Z)");
        decoded.Should().Contain($"_sprk_contact_value eq {FeedContact} and statecode eq 0 and (");
        decoded.Should().Contain("sprk_expiresdate ge 2026-10-12", "a lapsed grant is never announced, renewed or not");
        decoded.Should().Contain("sprk_granteddate,createdon,modifiedon");
        decoded.Should().Contain("$orderby=modifiedon desc,sprk_externalrecordaccessid desc",
            "a cap must cut the oldest activity, not the renewals (a renewed row keeps its old createdon)");
        decoded.Should().NotContain("$orderby=createdon");
    }

    [Fact]
    public async Task GetRecentGrants_WithoutModifiedSince_IsTheOriginalFilter()
    {
        var (service, handler) = NewService();

        await service.GetRecentGrantsAsync(FeedContact, new DateOnly(2026, 9, 12), new DateOnly(2026, 10, 12));

        Decoded(handler.GetUrls.Single()).Should().NotContain("modifiedon ge").And.NotContain("$orderby=modifiedon");
    }

    [Fact]
    public async Task GetRecentGrants_Parse_RenewedOnIsModifiedOn_OnlyWhenTheRowWasChangedWellAfterItWasCreated()
    {
        var (service, handler) = NewService();
        Guid created = Guid.NewGuid(), edited = Guid.NewGuid(), justAfter = Guid.NewGuid(), noCreated = Guid.NewGuid(), noModified = Guid.NewGuid();
        string Row(Guid id, string? createdOn, string? modifiedOn)
        {
            var parts = new List<string>
            {
                $"\"sprk_externalrecordaccessid\":\"{id}\"",
                $"\"_sprk_project_value\":\"{Guid.NewGuid()}\"",
                "\"sprk_granteddate\":\"2026-08-01\"",
            };
            if (createdOn is not null) parts.Add($"\"createdon\":\"{createdOn}\"");
            if (modifiedOn is not null) parts.Add($"\"modifiedon\":\"{modifiedOn}\"");
            return "{" + string.Join(",", parts) + "}";
        }

        handler.GetResponder = _ => JsonOk("{\"value\":[" + string.Join(",",
            Row(created, "2026-10-10T09:00:00Z", "2026-10-10T09:00:00Z"),       // never modified
            Row(edited, "2026-08-01T09:00:00Z", "2026-10-09T15:30:00Z"),       // changed long after creation
            Row(justAfter, "2026-10-10T09:00:00Z", "2026-10-10T09:01:30Z"),    // the creation's own follow-up write
            Row(noCreated, null, "2026-10-09T15:30:00Z"),                       // no createdon: cannot tell, so no renewal
            Row(noModified, "2026-08-01T09:00:00Z", null)) + "]}");

        var read = await service.GetRecentGrantsAsync(
            FeedContact, new DateOnly(2026, 9, 12), new DateOnly(2026, 10, 12),
            modifiedSince: new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));

        RenewedOf(read, created).Should().BeNull("modifiedon equals createdon");
        RenewedOf(read, edited).Should().Be(DateTimeOffset.Parse("2026-10-09T15:30:00Z"));
        RenewedOf(read, justAfter).Should().BeNull("within the creation gap: the grant's own follow-up write, not a renewal");
        RenewedOf(read, noCreated).Should().BeNull("without a real createdon every row would look renewed");
        RenewedOf(read, noModified).Should().BeNull();
        read.Grants.Single(g => g.GrantId == edited).EventOn.Should().Be(DateTimeOffset.Parse("2026-10-09T15:30:00Z"));
        read.Grants.Single(g => g.GrantId == created).EventOn.Should().Be(DateTimeOffset.Parse("2026-10-10T09:00:00Z"));

        static DateTimeOffset? RenewedOf(ExternalDataService.RecentGrantsRead r, Guid id) => r.Grants.Single(g => g.GrantId == id).RenewedOn;
    }

    [Fact]
    public async Task GetRecentGrants_EmptyContact_ReadsNothing()
    {
        var (service, handler) = NewService();

        var read = await service.GetRecentGrantsAsync(Guid.Empty, new DateOnly(2026, 9, 12), new DateOnly(2026, 10, 12));

        read.Grants.Should().BeEmpty();
        read.Truncated.Should().BeFalse();
        handler.GetUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentGrants_Parse_MapsRootsAndTimes_SkipsAmbiguousAndRootless()
    {
        var (service, handler) = NewService();
        Guid projectGrant = Guid.NewGuid(), matterGrant = Guid.NewGuid(), waGrant = Guid.NewGuid();
        Guid twoRoots = Guid.NewGuid(), noRoot = Guid.NewGuid(), noTime = Guid.NewGuid();
        Guid project = Guid.NewGuid(), matter = Guid.NewGuid(), wa = Guid.NewGuid();
        var rows = string.Join(",",
            GrantRow(projectGrant, project, null, null, "2026-10-10T09:12:00Z", "2026-10-10"),
            // No createdon: falls back to sprk_granteddate at 00:00 UTC.
            GrantRow(matterGrant, null, matter, null, null, "2026-10-01"),
            GrantRow(waGrant, null, null, wa, "2026-10-11T23:30:00Z", "2026-10-11"),
            // Two roots: not exactly one, skipped.
            GrantRow(twoRoots, Guid.NewGuid(), Guid.NewGuid(), null, "2026-10-10T09:12:00Z", "2026-10-10"),
            // The root was deleted and the lookup emptied: skipped.
            GrantRow(noRoot, null, null, null, "2026-10-10T09:12:00Z", "2026-10-10"),
            // Neither createdon nor sprk_granteddate: no time to report, skipped.
            GrantRow(noTime, Guid.NewGuid(), null, null, null, null));
        handler.GetResponder = _ => JsonOk("{\"value\":[" + rows + "]}");

        var read = await service.GetRecentGrantsAsync(FeedContact, new DateOnly(2026, 9, 12), new DateOnly(2026, 10, 12));

        read.Truncated.Should().BeFalse();
        read.Grants.Select(g => g.GrantId).Should().Equal(projectGrant, matterGrant, waGrant);
        read.Grants[0].Should().Be(new ExternalDataService.RecentGrant(
            projectGrant, ExternalGrantRootType.Project, project, DateTimeOffset.Parse("2026-10-10T09:12:00Z")));
        read.Grants[1].Should().Be(new ExternalDataService.RecentGrant(
            matterGrant, ExternalGrantRootType.Matter, matter, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)),
            "createdon is absent, so the GrantedOnValue fallback is sprk_granteddate at 00:00 UTC");
        read.Grants[2].RootType.Should().Be(ExternalGrantRootType.WorkAssignment);
        read.Grants[2].RootId.Should().Be(wa);
        read.Grants[2].GrantedOn.Should().Be(DateTimeOffset.Parse("2026-10-11T23:30:00Z"), "createdon, not the date, is the item time");
    }

    [Fact]
    public async Task GetRecentGrants_AFailedRead_IsEmptyAndFlaggedTruncated()
    {
        var (service, handler) = NewService();
        handler.GetResponder = _ => ServerError();

        var read = await service.GetRecentGrantsAsync(FeedContact, new DateOnly(2026, 9, 12), new DateOnly(2026, 10, 12));

        read.Grants.Should().BeEmpty();
        read.Truncated.Should().BeTrue("a Dataverse fault must never read as 'no grants'");
    }

    [Fact]
    public async Task GetServiceRequestsRequestedBy_FiltersOnTheRequesterColumn_ForEachDistinctNonEmptyId_AndParses()
    {
        var (service, handler) = NewService();
        Guid requesterA = Guid.NewGuid(), requesterB = Guid.NewGuid(), requestId = Guid.NewGuid(), unnamedId = Guid.NewGuid();
        handler.GetResponder = _ => JsonOk(
            "{\"value\":[" +
            $"{{\"sprk_servicerequestid\":\"{requestId}\",\"sprk_name\":\"Please review\",\"_sprk_requestedby_value\":\"{requesterA}\"}}," +
            $"{{\"sprk_servicerequestid\":\"{unnamedId}\",\"_sprk_requestedby_value\":\"{requesterB}\"}}," +
            // No requester: not attributable to anyone, dropped.
            $"{{\"sprk_servicerequestid\":\"{Guid.NewGuid()}\",\"sprk_name\":\"orphan\"}}" +
            "]}");

        var read = await service.GetServiceRequestsRequestedByAsync(new[] { requesterA, requesterB, requesterA, Guid.Empty });

        read.Truncated.Should().BeFalse();
        var url = handler.GetUrls.Should().ContainSingle().Subject;
        var decoded = Decoded(url);
        url.Should().Contain("/sprk_servicerequests?");
        decoded.Should().Contain($"_sprk_requestedby_value eq {requesterA}");
        decoded.Should().Contain($"_sprk_requestedby_value eq {requesterB}");
        decoded.Should().NotContain("00000000-0000-0000-0000-000000000000");
        decoded.Split($"_sprk_requestedby_value eq {requesterA}").Should().HaveCount(2, "a duplicate id is queried once");
        decoded.Should().Contain("$orderby=createdon desc");
        url.Should().NotContain("$top");
        read.Requests.Should().HaveCount(2);
        read.Requests[0].Should().Be(new ExternalDataService.RequestedServiceRequest(requestId, "Please review", requesterA));
        read.Requests[1].Should().Be(new ExternalDataService.RequestedServiceRequest(unnamedId, null, requesterB));
    }

    [Fact]
    public async Task GetServiceRequestsRequestedBy_NoIds_ReadsNothing_AndAFailedReadIsTruncated()
    {
        var (service, handler) = NewService();

        var none = await service.GetServiceRequestsRequestedByAsync(new[] { Guid.Empty });
        none.Requests.Should().BeEmpty();
        none.Truncated.Should().BeFalse();
        handler.GetUrls.Should().BeEmpty();

        handler.GetResponder = _ => ServerError();
        var failed = await service.GetServiceRequestsRequestedByAsync(new[] { Guid.NewGuid() });
        failed.Requests.Should().BeEmpty();
        failed.Truncated.Should().BeTrue();
    }

    [Theory]
    [InlineData("sprk_project", "sprk_projects", "sprk_projectid", "sprk_projectname")]
    [InlineData("sprk_matter", "sprk_matters", "sprk_matterid", "sprk_mattername")]
    [InlineData("sprk_workassignment", "sprk_workassignments", "sprk_workassignmentid", "sprk_name")]
    public async Task GetRecordNames_ReadsTheRightEntitySet_WithItsIdAndNameColumns(
        string entity, string entitySet, string idColumn, string nameColumn)
    {
        var (service, handler) = NewService();
        Guid id = Guid.NewGuid(), unnamed = Guid.NewGuid();
        handler.GetResponder = _ => JsonOk(
            $"{{\"value\":[{{\"{idColumn}\":\"{id}\",\"{nameColumn}\":\"Acme\"}},{{\"{idColumn}\":\"{unnamed}\",\"{nameColumn}\":null}}]}}");

        var read = await service.GetRecordNamesAsync(entity, new[] { id, unnamed });

        var url = handler.GetUrls.Should().ContainSingle().Subject;
        url.Should().Contain($"/{entitySet}?");
        var decoded = Decoded(url);
        decoded.Should().Contain($"{idColumn} eq {id}").And.Contain($"{idColumn} eq {unnamed}");
        decoded.Should().Contain($"$select={idColumn},{nameColumn}");
        url.Should().NotContain("statecode", "a deactivated record is kept; only a deleted one is dropped");
        read.Complete.Should().BeTrue();
        read.Names.Should().HaveCount(2);
        read.Names[id].Should().Be("Acme");
        read.Names[unnamed].Should().BeNull();
    }

    [Fact]
    public async Task GetRecordNames_ChunksTheIds_AndACompleteReadMeansAMissingIdIsGone()
    {
        var (service, handler) = NewService();
        var ids = Enumerable.Range(0, ExternalDataService.IdFilterChunkSize * 2 + 50).Select(_ => Guid.NewGuid()).ToList();
        var gone = ids[^1];
        handler.GetResponder = url =>
        {
            var d = Decoded(url);
            var rows = ids.Where(id => id != gone && d.Contains($"sprk_matterid eq {id}"))
                .Select(id => $"{{\"sprk_matterid\":\"{id}\",\"sprk_mattername\":\"M-{id:N}\"}}");
            return JsonOk("{\"value\":[" + string.Join(",", rows) + "]}");
        };

        var read = await service.GetRecordNamesAsync("sprk_matter", ids);

        handler.GetUrls.Should().HaveCount(3, "100 + 100 + 50 ids");
        handler.GetUrls.Should().OnlyContain(u => u.Contains("/sprk_matters?"));
        read.Complete.Should().BeTrue();
        read.Names.Should().HaveCount(ids.Count - 1);
        read.Names.Should().NotContainKey(gone, "a complete read that does not return an id means the record is gone");
    }

    [Fact]
    public async Task GetRecordNames_AFailedChunk_IsNotComplete_SoAMissingIdIsNotTreatedAsGone()
    {
        var (service, handler) = NewService();
        var ids = Enumerable.Range(0, ExternalDataService.IdFilterChunkSize + 5).Select(_ => Guid.NewGuid()).ToList();
        var calls = 0;
        handler.GetResponder = _ => ++calls == 2 ? ServerError() : JsonOk("{\"value\":[]}");

        var read = await service.GetRecordNamesAsync("sprk_project", ids);

        calls.Should().Be(2);
        read.Complete.Should().BeFalse();
    }

    [Fact]
    public async Task GetRecordNames_UnknownEntity_IsIncomplete_AndNoIds_IsCompleteWithoutARead()
    {
        var (service, handler) = NewService();

        var unknown = await service.GetRecordNamesAsync("sprk_invoice", new[] { Guid.NewGuid() });
        unknown.Names.Should().BeEmpty();
        unknown.Complete.Should().BeFalse("an entity the feed cannot title is not 'gone'");

        var none = await service.GetRecordNamesAsync("sprk_project", new[] { Guid.Empty });
        none.Names.Should().BeEmpty();
        none.Complete.Should().BeTrue();
        handler.GetUrls.Should().BeEmpty();
    }

    // =========================================================================
    // Task 064 (owner check 3, item 1): sprk_documenttype is a CHOICE column. Dataverse sends its NUMBER, so the row
    // used to be typed string? and ONE typed document failed the whole page ("could not be converted to System.String
    // ... token type 'Number'"), flagging the list truncated. The wire DTO still carries the LABEL, read from the
    // OData formatted-value annotation.
    // =========================================================================

    private const string DocumentTypeAnnotation = "sprk_documenttype@OData.Community.Display.V1.FormattedValue";

    private static readonly Guid DocumentRoot = Guid.Parse("06400000-0000-0000-0000-000000000001");

    private static readonly CallerPrincipal DocumentReader = new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.Parse("06400000-0000-0000-0000-0000000000c0"),
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(DocumentRoot, ExternalAccessLevel.ViewOnly) },
    };

    private static string DocumentRowJson(string id, string typeJson)
    {
        var typeProps = typeJson.Length == 0 ? "" : "," + typeJson;
        return $"{{\"sprk_documentid\":\"{id}\",\"sprk_documentname\":\"Doc {id}\",\"_sprk_project_value\":\"{DocumentRoot}\"{typeProps}}}";
    }

    private static HttpResponseMessage DocumentsOnly(string url, string body) =>
        Decoded(url).Contains("/sprk_documents?") ? JsonOk(body) : JsonOk("{\"value\":[]}");

    [Fact]
    public async Task DocumentList_ARowWithANumericType_IsReturnedWithTheFormattedLabel_AndTheListIsNotTruncated()
    {
        var (service, handler) = NewService();
        var typed = Guid.NewGuid().ToString();
        var untyped = Guid.NewGuid().ToString();
        handler.GetResponder = url => DocumentsOnly(url, "{\"value\":["
            + DocumentRowJson(typed, $"\"sprk_documenttype\":100000006,\"{DocumentTypeAnnotation}\":\"Email\"") + ","
            + DocumentRowJson(untyped, "\"sprk_documenttype\":null") + "]}");

        var list = await service.GetDocumentsAsync(ExternalDataService.DocumentParentKind.Project, DocumentRoot, DocumentReader);

        list.Truncated.Should().BeFalse("a typed document is no longer a failed page");
        list.Value.Should().HaveCount(2);
        list.Value.Single(d => d.SprkDocumentid == typed).SprkDocumenttype.Should().Be("Email");
        list.Value.Single(d => d.SprkDocumentid == untyped).SprkDocumenttype.Should().BeNull("an unset type is null");
    }

    [Fact]
    public async Task DocumentList_ARowWithNoTypeProperty_ReturnsANullType()
    {
        var (service, handler) = NewService();
        var id = Guid.NewGuid().ToString();
        handler.GetResponder = url => DocumentsOnly(url, "{\"value\":[" + DocumentRowJson(id, "") + "]}");

        var list = await service.GetDocumentsAsync(ExternalDataService.DocumentParentKind.Project, DocumentRoot, DocumentReader);

        list.Truncated.Should().BeFalse();
        list.Value.Should().ContainSingle().Which.SprkDocumenttype.Should().BeNull();
    }

    [Fact]
    public async Task DocumentList_ANumberWithNoAnnotation_IsNull_NeverTheNumber()
    {
        var (service, handler) = NewService();
        var id = Guid.NewGuid().ToString();
        handler.GetResponder = url => DocumentsOnly(url, "{\"value\":[" + DocumentRowJson(id, "\"sprk_documenttype\":100000006") + "]}");

        var list = await service.GetDocumentsAsync(ExternalDataService.DocumentParentKind.Project, DocumentRoot, DocumentReader);

        list.Truncated.Should().BeFalse("the row itself is readable");
        list.Value.Should().ContainSingle().Which.SprkDocumenttype.Should().BeNull("the number is never shown as a label");
    }

    // =========================================================================
    // R3 ISS-011 (#1886, owner decision 2026-10-14): the document list says whether each row has a file, so the SPA can
    // list a file-less document without Download or Versions. Derived from the SPE pointers the list now selects, by the
    // same checks the download route applies; only the boolean is on the wire.
    // =========================================================================

    private const string UsableDriveId = "b!OWdlYTJhZGUtYWFkZC00NDkxLWE1NjctMTIzNDU2Nzg5MGFi";
    private const string UsableItemId = "01LBYCMX76QPLGITR47BB355T4G2CVDL2B";

    [Fact]
    public async Task DocumentList_HasFile_IsTrueForARowWithPointers_AndFalseForARowWithNone()
    {
        var (service, handler) = NewService();
        var withFile = Guid.NewGuid().ToString();
        var noPointers = Guid.NewGuid().ToString();
        var blankPointers = Guid.NewGuid().ToString();
        handler.GetResponder = url => DocumentsOnly(url, "{\"value\":["
            + DocumentRowJson(withFile, $"\"sprk_graphdriveid\":\"{UsableDriveId}\",\"sprk_graphitemid\":\"{UsableItemId}\"") + ","
            + DocumentRowJson(noPointers, "") + ","
            + DocumentRowJson(blankPointers, "\"sprk_graphdriveid\":null,\"sprk_graphitemid\":\"\"") + "]}");

        var list = await service.GetDocumentsAsync(ExternalDataService.DocumentParentKind.Project, DocumentRoot, DocumentReader);

        list.Value.Should().HaveCount(3, "a document with no file is still SHOWN (owner decision); only its actions are withheld");
        list.Value.Single(d => d.SprkDocumentid == withFile).HasFile.Should().BeTrue();
        list.Value.Single(d => d.SprkDocumentid == noPointers).HasFile.Should().BeFalse();
        list.Value.Single(d => d.SprkDocumentid == blankPointers).HasFile.Should().BeFalse();
    }

    [Fact]
    public async Task DocumentList_HasFile_IsOnTheWireAsABoolean_AndNoPointerIs()
    {
        var (service, handler) = NewService();
        var id = Guid.NewGuid().ToString();
        handler.GetResponder = url => DocumentsOnly(url, "{\"value\":["
            + DocumentRowJson(id, $"\"sprk_graphdriveid\":\"{UsableDriveId}\",\"sprk_graphitemid\":\"{UsableItemId}\"") + "]}");

        var list = await service.GetDocumentsAsync(ExternalDataService.DocumentParentKind.Project, DocumentRoot, DocumentReader);

        var json = JsonSerializer.Serialize(list.Value);
        json.Should().Contain("\"hasFile\":true");
        json.Should().NotContain("graph").And.NotContain(UsableItemId).And.NotContain(UsableDriveId, "no Graph pointer reaches the client");
    }

    [Fact]
    public void BuildDocumentListUrl_SelectsTheSpePointers_ThatHasFileIsDerivedFrom()
    {
        var select = Uri.UnescapeDataString(ExternalDataService.BuildDocumentListUrl(
            "https://org.test/api/data/v9.2", ExternalDataService.DocumentParentKind.Project, DocumentRoot)).Split(',');

        select.Should().Contain(new[] { "sprk_graphdriveid", "sprk_graphitemid" });
    }

    [Fact]
    public async Task EveryCollectionPage_AsksForServerPaging_AndTheFormattedValueAnnotations()
    {
        var (service, handler) = NewService();
        handler.GetResponder = url => DocumentsOnly(url, "{\"value\":[]}");

        await service.GetDocumentsAsync(ExternalDataService.DocumentParentKind.Project, DocumentRoot, DocumentReader);

        var documentReads = handler.GetPrefers.Where(p => p.Url.Contains("/sprk_documents?")).ToList();
        documentReads.Should().NotBeEmpty();
        documentReads.Should().OnlyContain(p =>
            p.Prefer.Contains($"odata.maxpagesize={ExternalDataService.CollectionPageSize}")
            && p.Prefer.Contains("odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\""));
    }

    [Fact]
    public async Task DocumentList_ANon2xxPage_StillReturnsEmptyFlaggedTruncated()
    {
        var (service, handler) = NewService();
        handler.GetResponder = url => Decoded(url).Contains("/sprk_documents?") ? ServerError() : JsonOk("{\"value\":[]}");

        var list = await service.GetDocumentsAsync(ExternalDataService.DocumentParentKind.Project, DocumentRoot, DocumentReader);

        list.Value.Should().BeEmpty();
        list.Truncated.Should().BeTrue("a genuine Dataverse failure is unchanged: empty and flagged, never an empty list");
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

        /// <summary>Every request that reached the transport, whatever its method.</summary>
        public List<HttpMethod> Requests { get; } = new();

        /// <summary>The absolute URL of every GET, in order (task 037 review F3-b: the feed reads assert their queries).</summary>
        public List<string> GetUrls { get; } = new();

        /// <summary>The URL and <c>Prefer</c> header value of every GET (task 064: the collection reads' Prefer is asserted).</summary>
        public List<(string Url, string Prefer)> GetPrefers { get; } = new();

        /// <summary>When set, answers each GET (by its absolute URL); otherwise a GET is an empty collection.</summary>
        public Func<string, HttpResponseMessage>? GetResponder { get; set; }

        public IReadOnlyDictionary<string, string?> SinglePostBody()
        {
            PostBodies.Should().ContainSingle("exactly one create is posted");
            using var doc = JsonDocument.Parse(PostBodies[0]);
            return doc.RootElement.EnumerateObject().ToDictionary(
                p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText());
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.Method);
            if (request.Method == HttpMethod.Post)
            {
                PostBodies.Add(await request.Content!.ReadAsStringAsync(ct));
                var id = Guid.NewGuid().ToString("D");
                return Json($"{{\"sprk_todoid\":\"{id}\",\"sprk_eventid\":\"{id}\",\"sprk_documentid\":\"{id}\"}}");
            }

            if (request.Method == HttpMethod.Get)
            {
                var url = request.RequestUri!.AbsoluteUri;
                GetUrls.Add(url);
                GetPrefers.Add((url, request.Headers.TryGetValues("Prefer", out var prefer) ? string.Join(",", prefer) : ""));
                if (GetResponder is { } responder)
                    return responder(url);
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
