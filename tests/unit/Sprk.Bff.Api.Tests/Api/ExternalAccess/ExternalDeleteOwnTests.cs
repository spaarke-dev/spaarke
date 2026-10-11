using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDataService;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// spaarke-SPA-external-access-platform-r3 task 032 (FR-26): an external caller hard-deletes a document, to-do or event
/// it CREATED, while it still holds Read on the record's parent, through the REAL <c>DELETE /api/v1/external/...</c>
/// routes and handlers.
/// </summary>
/// <remarks>
/// <para>Doubles sit at module boundaries only: the caller principal, <see cref="ExternalDataService"/>'s two delete-own
/// seams and the requester read (in memory), Dataverse's document endpoint (a hand-written handler under the REAL
/// <see cref="DocumentCheckoutService"/>, the internal plane's delete, reused), and <see cref="SpeFileStore.DeleteFileAsync"/>.
/// The pointer check is the REAL <c>RecordContainerResolver</c> over a
/// world whose business unit claims every <c>drive-*</c> container.</para>
/// <para>Service requests have no delete route: task 032 escalated it (the request's related rows, some created by staff,
/// are only unlinked by Dataverse — <c>CascadeDelete=RemoveLink</c>).</para>
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalDeleteOwnTests : IClassFixture<ExternalDeleteOwnTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999032");
    private static readonly Guid OtherContact = Guid.Parse("88888888-8888-8888-8888-888888888032");
    private static readonly Guid CallerUser = Guid.Parse("77777777-7777-7777-7777-777777777032");
    private static readonly Guid OtherUser = Guid.Parse("66666666-6666-6666-6666-666666666032");

    private static readonly Guid RecordA = Guid.Parse("a0320000-0000-4000-8000-00000000000a");
    private static readonly Guid RecordB = Guid.Parse("a0320000-0000-4000-8000-00000000000b");
    private static readonly Guid RequestA = Guid.Parse("a0320000-0000-4000-8000-0000000000c1");

    private readonly Fixture _fixture;

    public ExternalDeleteOwnTests(Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    public static TheoryData<string> AllKinds => new() { "documents", "todos", "events" };

    // =====================================================================
    // AC 1 + 2: the creator deletes; a document's SPE file goes first, then the row
    // =====================================================================

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task Creator_Deletes_204_AndTheRowIsGone(string segment)
    {
        var id = _fixture.Seed(KindOf(segment), contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);

        using var response = await Delete(segment, id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        _fixture.Exists(KindOf(segment), id).Should().BeFalse();
    }

    [Fact]
    public async Task Document_DeletesTheSpeFile_ThenTheRow()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);

        using var response = await Delete("documents", id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.Steps.Should().Equal(
            $"file-delete:drive-{id:N}/item-{id:N}",
            $"row-delete:{id}");
    }

    [Fact]
    public async Task Creator_WithViewOnly_StillDeletes_ReadOnTheParentIsTheRule()
    {
        var id = _fixture.Seed(DeletableKind.Todo, contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.ViewOnly);

        using var response = await Delete("todos", id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Creator_OnAMatter_AndAWorkAssignment_Deletes()
    {
        var onMatter = _fixture.Seed(DeletableKind.Event, contact: CallerContact,
            parents: new DeleteTargetParent(DocumentParentKind.Matter, RecordA));
        var onAssignment = _fixture.Seed(DeletableKind.Document, contact: CallerContact,
            parents: new DeleteTargetParent(DocumentParentKind.WorkAssignment, RecordB));
        _fixture.Principal = Contact(
            matters: new Dictionary<Guid, AccessRights> { [RecordA] = AccessRights.Read },
            workAssignments: new Dictionary<Guid, AccessRights> { [RecordB] = AccessRights.Read });

        using var matter = await Delete("events", onMatter);
        using var assignment = await Delete("documents", onAssignment);

        matter.StatusCode.Should().Be(HttpStatusCode.NoContent);
        assignment.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // =====================================================================
    // AC 3: another user, even with FullAccess on the parent, is refused (the seeding-proof test)
    // =====================================================================

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task OtherUser_WithFullAccessOnTheParent_Is403_AndNothingIsDeleted(string segment)
    {
        var id = _fixture.Seed(KindOf(segment), contact: OtherContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.FullAccess);

        using var response = await Delete(segment, id);

        await ShouldBeTheUniformDenial(response);
        _fixture.Exists(KindOf(segment), id).Should().BeTrue();
        _fixture.Steps.Should().BeEmpty("no file and no row delete is sent for a refused caller");
    }

    // =====================================================================
    // AC 4: a creator who lost Read on the parent is refused
    // =====================================================================

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task Creator_WhoLostReadOnTheParent_Is403(string segment)
    {
        var id = _fixture.Seed(KindOf(segment), contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordB, ExternalAccessLevel.FullAccess); // access to another project only

        using var response = await Delete(segment, id);

        await ShouldBeTheUniformDenial(response);
        _fixture.Exists(KindOf(segment), id).Should().BeTrue();
        _fixture.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task Document_FiledToTwoParents_NeedsReadOnBoth()
    {
        // Filed to the caller's project AND to a service request it did not request.
        _fixture.Data.Requesters[RequestA] = OtherContact;
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact,
            parents: new[] { Project(RecordA), new DeleteTargetParent(DocumentParentKind.ServiceRequest, RequestA) });
        _fixture.Principal = WorkforceContactOn(RecordA);

        using var refused = await Delete("documents", id);
        await ShouldBeTheUniformDenial(refused);
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();

        // Once the caller is the requester, both parents are readable and the delete goes through.
        _fixture.Data.Requesters[RequestA] = CallerContact;
        using var allowed = await Delete("documents", id);
        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Row_FiledToNoParent_Is403()
    {
        var id = _fixture.Seed(DeletableKind.Todo, contact: CallerContact, parents: Array.Empty<DeleteTargetParent>());
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.FullAccess);

        using var response = await Delete("todos", id);

        await ShouldBeTheUniformDenial(response);
        _fixture.Exists(DeletableKind.Todo, id).Should().BeTrue();
    }

    // =====================================================================
    // AC 5: an unstamped row is nobody's
    // =====================================================================

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task UnstampedRow_Is403(string segment)
    {
        var id = _fixture.Seed(KindOf(segment), parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.FullAccess);

        using var response = await Delete(segment, id);

        await ShouldBeTheUniformDenial(response);
        _fixture.Exists(KindOf(segment), id).Should().BeTrue();
    }

    // =====================================================================
    // AC 6: the principal kind decides which stamp matches (owner decision (b) widened)
    // =====================================================================

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task ContactCaller_CannotDeleteAPersonStampedRow(string segment)
    {
        // Even a person stamp whose GUID equals the caller's contact id: the kinds never cross.
        var id = _fixture.Seed(KindOf(segment), person: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.FullAccess);

        using var response = await Delete(segment, id);

        await ShouldBeTheUniformDenial(response);
        _fixture.Exists(KindOf(segment), id).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task SystemUser_CannotDeleteARowStampedWithSomeoneElsesContact(string segment)
    {
        var id = _fixture.Seed(KindOf(segment), contact: OtherContact, parents: Project(RecordA));
        _fixture.Principal = SystemUserOn(RecordA, linkedContact: CallerContact);

        using var response = await Delete(segment, id);

        await ShouldBeTheUniformDenial(response);
        _fixture.Exists(KindOf(segment), id).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task SystemUser_DeletesARowStampedWithItsOwnLinkedContact(string segment)
    {
        var id = _fixture.Seed(KindOf(segment), contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = SystemUserOn(RecordA, linkedContact: CallerContact);

        using var response = await Delete(segment, id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.Exists(KindOf(segment), id).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task SystemUser_DeletesItsOwnPersonStampedRow_ButNotAnotherPersons(string segment)
    {
        var mine = _fixture.Seed(KindOf(segment), person: CallerUser, parents: Project(RecordA));
        var theirs = _fixture.Seed(KindOf(segment), person: OtherUser, parents: Project(RecordA));
        _fixture.Principal = SystemUserOn(RecordA, linkedContact: CallerContact);

        using var own = await Delete(segment, mine);
        using var other = await Delete(segment, theirs);

        own.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await ShouldBeTheUniformDenial(other);
        _fixture.Exists(KindOf(segment), theirs).Should().BeTrue();
    }

    // =====================================================================
    // No existence oracle: absent and not-yours answer identically
    // =====================================================================

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task AbsentRow_AnswersExactlyAsSomeoneElsesRow(string segment)
    {
        var theirs = _fixture.Seed(KindOf(segment), contact: OtherContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.FullAccess);

        using var absent = await Delete(segment, Guid.NewGuid());
        using var notMine = await Delete(segment, theirs);

        absent.StatusCode.Should().Be(notMine.StatusCode);
        Problem(await absent.Content.ReadAsStringAsync()).Should().Be(Problem(await notMine.Content.ReadAsStringAsync()));
    }

    // =====================================================================
    // AC 8: a document whose file cannot be deleted is kept, and the caller is told
    // =====================================================================

    [Fact]
    public async Task Document_WhenTheSpeDeleteFails_KeepsTheRow_And503()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);
        _fixture.FileFault = new InvalidOperationException("Graph said 503");

        using var response = await Delete("documents", id);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        ProblemCode(await response.Content.ReadAsStringAsync()).Should().Be(DocumentCheckoutService.FileNotDeletedCode);
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue("the row is kept when its file was not deleted");
        _fixture.Steps.Should().Equal($"file-delete:drive-{id:N}/item-{id:N}");
    }

    [Fact]
    public async Task Document_WhosePointerIsRefused_KeepsTheRow_And409_AndTheFileIsNotTouched()
    {
        // A pointer into a container no business unit or record of this world claims.
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA),
            driveId: "b!foreign-container");
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);

        using var response = await Delete("documents", id);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
        _fixture.Steps.Should().BeEmpty("a refused pointer is never followed");
    }

    [Fact]
    public async Task Document_WhoseFileIsAlreadyGone_IsDeleted()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);
        _fixture.FileAlreadyGone = true;

        using var response = await Delete("documents", id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.Exists(DeletableKind.Document, id).Should().BeFalse();
    }

    [Fact]
    public async Task Document_WhenTheRowDeleteFailsAfterTheFile_Is500_NotASuccess()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);
        _fixture.Dataverse.RowDeleteStatus = HttpStatusCode.ServiceUnavailable;

        using var response = await Delete("documents", id);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        ProblemCode(await response.Content.ReadAsStringAsync()).Should().Be("document_row_not_deleted");
    }

    [Fact]
    public async Task Document_CheckedOut_Is409_WithoutTheLockHoldersName_AndNothingIsDeleted()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Dataverse.CheckOut(id, "Paula Partner");
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);

        using var response = await Delete("documents", id);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        ProblemCode(body).Should().Be("document_locked");
        body.Should().NotContain("Paula");
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
        _fixture.Steps.Should().BeEmpty();
    }

    // =====================================================================
    // Faults: "could not tell" is a 503, never a 403 and never a delete
    // =====================================================================

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task TargetReadFault_Is503_AndNothingIsDeleted(string segment)
    {
        var id = _fixture.Seed(KindOf(segment), contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);
        _fixture.Data.TargetFault = new DataverseUnavailableException("throttled");

        using var response = await Delete(segment, id);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        ReasonCode(await response.Content.ReadAsStringAsync()).Should().Be("sdap.access.delete_own_unavailable");
        _fixture.Exists(KindOf(segment), id).Should().BeTrue();
        _fixture.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task RequesterReadFault_OnAServiceRequestParent_Is503_AndNothingIsDeleted()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact,
            parents: new DeleteTargetParent(DocumentParentKind.ServiceRequest, RequestA));
        _fixture.Principal = WorkforceContactOn(RecordA);
        _fixture.Data.RequesterFault = new DataverseUnavailableException("throttled");

        using var response = await Delete("documents", id);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
        _fixture.Steps.Should().BeEmpty();
    }

    [Theory]
    [InlineData("todos")]
    [InlineData("events")]
    public async Task RowDeleteFault_Is503(string segment)
    {
        var id = _fixture.Seed(KindOf(segment), contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);
        _fixture.Data.DeleteFault = new DataverseUnavailableException("throttled");

        using var response = await Delete(segment, id);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _fixture.Exists(KindOf(segment), id).Should().BeTrue();
    }

    // =====================================================================
    // AC 7: no DELETE route for communications (messages), nor for service requests (escalated)
    // =====================================================================

    [Fact]
    public void TheExternalPlane_MapsExactlyThreeDeleteRoutes()
    {
        var deletes = _fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/external", StringComparison.OrdinalIgnoreCase) == true)
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods.Contains("DELETE") == true)
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        deletes.Should().BeEquivalentTo(
            "/api/v1/external/documents/{id:guid}",
            "/api/v1/external/todos/{id:guid}",
            "/api/v1/external/events/{id:guid}");
    }

    [Theory]
    [InlineData("messages")]
    [InlineData("communications")]
    [InlineData("servicerequests")]
    public async Task NoDeleteRoute_ForMessagesOrServiceRequests(string segment)
    {
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.FullAccess);

        using var response = await Delete(segment, Guid.NewGuid());

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    // =====================================================================
    // The stamp rule, the read and the delete seam (pure / transport level)
    // =====================================================================

    public static TheoryData<string, Guid?, Guid?, bool> StampCases => new()
    {
        // caller, person stamp, contact stamp, expected
        { "contact", null, CallerContact, true },
        { "contact", null, OtherContact, false },
        { "contact", CallerContact, null, false },     // a contact never matches a person stamp
        { "contact", null, null, false },              // unstamped
        { "systemuser", CallerUser, null, true },
        { "systemuser", OtherUser, null, false },
        { "systemuser", null, CallerContact, true },   // its own linked contact (owner decision (b) widened)
        { "systemuser", null, OtherContact, false },
        { "systemuser", null, null, false },
        { "unlinked-systemuser", null, null, false },  // ContactId empty never matches an empty-ish stamp
        { "unlinked-systemuser", CallerUser, null, true },
    };

    [Theory]
    [MemberData(nameof(StampCases))]
    public void IsCreatedByCaller_FollowsThePrincipalKind(string callerKind, Guid? person, Guid? contact, bool expected)
    {
        var caller = callerKind switch
        {
            "contact" => Contact(),
            "systemuser" => SystemUserOn(RecordA, CallerContact),
            _ => SystemUserOn(RecordA, Guid.Empty),
        };

        IsCreatedByCaller(new DeleteTarget(person, contact, new[] { Project(RecordA) }), caller).Should().Be(expected);
    }

    [Fact]
    public void IsCreatedByCaller_AnEmptyContactStamp_NeverMatchesAnUnlinkedSystemUser()
    {
        IsCreatedByCaller(new DeleteTarget(null, Guid.Empty, Array.Empty<DeleteTargetParent>()), SystemUserOn(RecordA, Guid.Empty))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(DeletableKind.Document, "sprk_documents",
        "_sprk_createdbyperson_value,_sprk_createdbycontact_value,_sprk_project_value,_sprk_matter_value,_sprk_workassignment_value,_sprk_relatedservicerequest_value")]
    [InlineData(DeletableKind.Todo, "sprk_todos",
        "_sprk_createdbyperson_value,_sprk_createdbycontact_value,_sprk_regardingproject_value,_sprk_regardingmatter_value,_sprk_regardingworkassignment_value")]
    [InlineData(DeletableKind.Event, "sprk_events",
        "_sprk_createdbyperson_value,_sprk_createdbycontact_value,_sprk_regardingproject_value,_sprk_regardingmatter_value,_sprk_regardingworkassignment_value")]
    public void DeleteTargetUrl_SelectsBothStampsAndEveryParentLookup(DeletableKind kind, string entitySet, string select)
    {
        BuildDeleteTargetUrl("https://org.test/api/data/v9.2", kind, RecordA)
            .Should().Be($"https://org.test/api/data/v9.2/{entitySet}({RecordA})?$select={select}");
    }

    [Fact]
    public async Task DeleteTargetRead_ParsesStampsAndParents_404IsNull_AndAFailureThrows()
    {
        var row = new Dictionary<string, object?>
        {
            ["_sprk_createdbycontact_value"] = CallerContact.ToString(),
            ["_sprk_createdbyperson_value"] = null,
            ["_sprk_project_value"] = RecordA.ToString(),
            ["_sprk_relatedservicerequest_value"] = RequestA.ToString(),
        };

        var target = await NewService(new JsonRowHandler(row)).GetDeleteTargetAsync(DeletableKind.Document, RecordB);

        target!.CreatedByContact.Should().Be(CallerContact);
        target.CreatedByPerson.Should().BeNull();
        target.Parents.Should().BeEquivalentTo(new[]
        {
            Project(RecordA), new DeleteTargetParent(DocumentParentKind.ServiceRequest, RequestA),
        });

        (await NewService(new StatusHandler(HttpStatusCode.NotFound)).GetDeleteTargetAsync(DeletableKind.Todo, RecordB))
            .Should().BeNull();

        // Until task 020's column is imported Dataverse answers 400: fail closed, never "not yours".
        var unimported = () => NewService(new StatusHandler(HttpStatusCode.BadRequest)).GetDeleteTargetAsync(DeletableKind.Event, RecordB);
        await unimported.Should().ThrowAsync<DataverseUnavailableException>();
    }

    [Theory]
    [InlineData(DeletableKind.Todo, "sprk_todos")]
    [InlineData(DeletableKind.Event, "sprk_events")]
    public async Task DeleteRecord_SendsOneDelete_404IsFalse_AndAFailureThrows(DeletableKind kind, string entitySet)
    {
        var ok = new StatusHandler(HttpStatusCode.NoContent);
        (await NewService(ok).DeleteRecordAsync(kind, RecordA)).Should().BeTrue();
        ok.Requests.Should().ContainSingle().Which.Should().Be($"DELETE https://org.test/api/data/v9.2/{entitySet}({RecordA})");

        (await NewService(new StatusHandler(HttpStatusCode.NotFound)).DeleteRecordAsync(kind, RecordA)).Should().BeFalse();

        var failed = () => NewService(new StatusHandler(HttpStatusCode.InternalServerError)).DeleteRecordAsync(kind, RecordA);
        await failed.Should().ThrowAsync<DataverseUnavailableException>();
    }

    [Fact]
    public async Task DeleteRecord_RefusesADocument_WhichMustGoThroughTheFileDelete()
    {
        var handler = new StatusHandler(HttpStatusCode.NoContent);

        var act = () => NewService(handler).DeleteRecordAsync(DeletableKind.Document, RecordA);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task InternalDelete_BestEffort_IsUnchanged_AFileFaultStillDeletesTheRow()
    {
        // The internal DELETE /api/documents/{id} keeps its behaviour: the strict policy is opt-in for the external route.
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.FileFault = new InvalidOperationException("Graph said 503");
        var service = _fixture.Services.GetRequiredService<DocumentCheckoutService>();

        var result = await service.DeleteAsync(id, "corr-032");

        result.Should().BeOfType<SuccessDeleteResult>();
        _fixture.Exists(DeletableKind.Document, id).Should().BeFalse();
    }

    // =====================================================================
    // Review 031 K2: reads return both creator stamps
    // =====================================================================

    [Fact]
    public void ListSelects_CarryBothCreatorStamps()
    {
        const string api = "https://org.test/api/data/v9.2";
        var documents = Uri.UnescapeDataString(BuildDocumentListUrl(api, DocumentParentKind.Matter, RecordA));
        documents.Should().Contain("_sprk_createdbyperson_value").And.Contain("_sprk_createdbycontact_value");
    }

    [Fact]
    public void Dtos_SerializeBothCreatorStamps()
    {
        var document = JsonSerializer.Serialize(new ExternalDocumentDto
        {
            SprkCreatedbypersonValue = CallerUser.ToString(), SprkCreatedbycontactValue = CallerContact.ToString(),
        });
        var todo = JsonSerializer.Serialize(new ExternalTodoDto { SprkCreatedbycontactValue = CallerContact.ToString() });
        var evt = JsonSerializer.Serialize(new ExternalEventDto { SprkCreatedbypersonValue = CallerUser.ToString() });

        document.Should().Contain($"\"_sprk_createdbyperson_value\":\"{CallerUser}\"")
            .And.Contain($"\"_sprk_createdbycontact_value\":\"{CallerContact}\"");
        todo.Should().Contain($"\"_sprk_createdbycontact_value\":\"{CallerContact}\"");
        evt.Should().Contain($"\"_sprk_createdbyperson_value\":\"{CallerUser}\"");
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private async Task<HttpResponseMessage> Delete(string segment, Guid id)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.DeleteAsync($"/api/v1/external/{segment}/{id}");
    }

    private static async Task ShouldBeTheUniformDenial(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
        ReasonCode(body).Should().Be("sdap.access.deny.delete_own");
    }

    private static string? ReasonCode(string body) =>
        JsonDocument.Parse(body).RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;

    private static string? ProblemCode(string body) =>
        JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;

    /// <summary>A problem body without its per-request fields (trace id), for an identical-answer comparison.</summary>
    private static string Problem(string body)
    {
        var root = JsonDocument.Parse(body).RootElement;
        return string.Join("|", root.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "instance" or "correlationId"))
            .Select(p => $"{p.Name}={p.Value}"));
    }

    private static DeletableKind KindOf(string segment) => segment switch
    {
        "documents" => DeletableKind.Document,
        "todos" => DeletableKind.Todo,
        _ => DeletableKind.Event,
    };

    private static DeleteTargetParent Project(Guid id) => new(DocumentParentKind.Project, id);

    private static CallerPrincipal Contact(
        IReadOnlyList<CallerProjectAccess>? projects = null,
        IReadOnlyDictionary<Guid, AccessRights>? matters = null,
        IReadOnlyDictionary<Guid, AccessRights>? workAssignments = null) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = CallerContact,
        Email = "external.user@example.test",
        ProjectAccess = projects ?? Array.Empty<CallerProjectAccess>(),
        MatterAccess = matters ?? new Dictionary<Guid, AccessRights>(),
        WorkAssignmentAccess = workAssignments ?? new Dictionary<Guid, AccessRights>(),
    };

    private static CallerPrincipal ContactOn(Guid project, ExternalAccessLevel level) =>
        Contact(projects: new[] { CallerProjectAccess.FromLevel(project, level) });

    /// <summary>A workforce contact (no Dataverse licence): can be a service request's requester.</summary>
    private static CallerPrincipal WorkforceContactOn(Guid project) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = CallerContact,
        Email = "staff@customer.test",
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(project, ExternalAccessLevel.Collaborate) },
    };

    /// <summary>A licensed systemuser whose server-resolved linked contact is <paramref name="linkedContact"/>.</summary>
    private static CallerPrincipal SystemUserOn(Guid project, Guid linkedContact) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        SystemUserId = CallerUser,
        ContactId = linkedContact,
        Email = "licensed@customer.test",
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(project, ExternalAccessLevel.Collaborate) },
    };

    private static ExternalDataService NewService(HttpMessageHandler transport) => new(
        new HttpClient(transport),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
            .Build(),
        new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential(),
        NullLogger<ExternalDataService>.Instance);

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add($"{request.Method} {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    /// <summary>Answers a single-row GET with <paramref name="row"/> as its JSON body.</summary>
    private sealed class JsonRowHandler(IReadOnlyDictionary<string, object?> row) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(row), System.Text.Encoding.UTF8, "application/json"),
            });
    }

    // =====================================================================
    // Fixture
    // =====================================================================

    /// <summary>
    /// The external collaboration host with the delete routes' module-boundary doubles. Every seeded row lives in ONE
    /// place per kind: a to-do or event in <see cref="DeleteOwnDataService"/>; a document in
    /// <see cref="DocumentRowsHandler"/> (what the REAL <see cref="DocumentCheckoutService"/> reads and deletes), with its
    /// delete-own facts mirrored in the data service.
    /// </summary>
    public sealed class Fixture : ExternalCollaborationTestFixture
    {
        private readonly ExternalTodoScopeTestFixture.StubCallerPrincipalResolver _principals = new();

        public DeleteOwnDataService Data { get; } = new();

        public DocumentRowsHandler Dataverse { get; } = new();

        /// <summary>Every destructive step, in order: <c>file-delete:{drive}/{item}</c> and <c>row-delete:{id}</c>.</summary>
        public ConcurrentQueue<string> Steps { get; } = new();

        /// <summary>Set to make <see cref="SpeFileStore.DeleteFileAsync"/> throw (Graph unavailable).</summary>
        public Exception? FileFault { get; set; }

        /// <summary>Set to make <see cref="SpeFileStore.DeleteFileAsync"/> report the item as already gone.</summary>
        public bool FileAlreadyGone { get; set; }

        public CallerPrincipal? Principal
        {
            get => _principals.Principal;
            set => _principals.Principal = value;
        }

        public void Reset()
        {
            Data.Reset();
            Dataverse.Reset();
            Steps.Clear();
            Principal = null;
            FileFault = null;
            FileAlreadyGone = false;
        }

        /// <summary>Seeds a deletable row with the given stamps and parents; a document also gets a row and a file.</summary>
        public Guid Seed(DeletableKind kind, Guid? person = null, Guid? contact = null,
            params DeleteTargetParent[] parents)
        {
            var id = Guid.NewGuid();
            Data.Targets[(kind, id)] = new DeleteTarget(person, contact, parents);
            if (kind == DeletableKind.Document)
                Dataverse.Add(id, $"drive-{id:N}", $"item-{id:N}");
            return id;
        }

        /// <summary><see cref="Seed(DeletableKind, Guid?, Guid?, DeleteTargetParent[])"/> with a chosen drive.</summary>
        public Guid Seed(DeletableKind kind, Guid? contact, DeleteTargetParent parents, string driveId)
        {
            var id = Guid.NewGuid();
            Data.Targets[(kind, id)] = new DeleteTarget(null, contact, new[] { parents });
            Dataverse.Add(id, driveId, $"item-{id:N}");
            return id;
        }

        public bool Exists(DeletableKind kind, Guid id) =>
            kind == DeletableKind.Document ? Dataverse.Contains(id) : Data.Targets.ContainsKey((kind, id));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ICallerPrincipalResolver>(_ => _principals);
                services.AddScoped(_ => (ExternalDataService)Data);

                Dataverse.Steps = Steps;
                services.RemoveAll<SpeFileStore>();
                services.AddSingleton<SpeFileStore>(sp => new RecordingSpeFileStore(sp, this));

                // The REAL internal-plane delete, over Dataverse's document endpoint in memory and the REAL pointer check
                // (a world whose business unit claims every "drive-*" container).
                services.RemoveAll<DocumentCheckoutService>();
                services.AddSingleton(sp => new DocumentCheckoutService(
                    new HttpClient(Dataverse),
                    sp.GetRequiredService<SpeFileStore>(),
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
                        .Build(),
                    new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential(),
                    new RecordOwnershipResolverDouble(),
                    NullLogger<DocumentCheckoutService>.Instance,
                    TestRecordContainerResolver.ForBusinessUnitContainers(c => c.StartsWith("drive-", StringComparison.Ordinal))));
            });
        }

        /// <summary>The SPE facade's delete, recorded and scripted; everything else is the real class.</summary>
        private sealed class RecordingSpeFileStore : SpeFileStore
        {
            private readonly Fixture _fixture;

            public RecordingSpeFileStore(IServiceProvider sp, Fixture fixture)
                : base(sp.GetRequiredService<ContainerOperations>(),
                       sp.GetRequiredService<DriveItemOperations>(),
                       sp.GetRequiredService<UploadSessionManager>(),
                       sp.GetRequiredService<UserOperations>())
            {
                _fixture = fixture;
            }

            public override Task<bool> DeleteFileAsync(string driveId, string itemId, CancellationToken ct = default)
            {
                _fixture.Steps.Enqueue($"file-delete:{driveId}/{itemId}");
                if (_fixture.FileFault is { } fault)
                    return Task.FromException<bool>(fault);
                return Task.FromResult(!_fixture.FileAlreadyGone);
            }
        }
    }

    /// <summary>
    /// Dataverse's <c>sprk_documents</c> endpoint as <see cref="DocumentCheckoutService"/> uses it: a single-row GET and a
    /// DELETE. A hand-written handler, not <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038 B1).
    /// </summary>
    public sealed class DocumentRowsHandler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<Guid, Dictionary<string, object?>> _rows = new();

        public ConcurrentQueue<string> Steps { get; set; } = new();

        /// <summary>Set to make the row DELETE fail with this status.</summary>
        public HttpStatusCode? RowDeleteStatus { get; set; }

        public void Reset()
        {
            _rows.Clear();
            RowDeleteStatus = null;
        }

        public void Add(Guid id, string driveId, string itemId) => _rows[id] = new Dictionary<string, object?>
        {
            ["sprk_documentid"] = id.ToString(),
            ["sprk_documentname"] = "brief.pdf",
            ["sprk_graphdriveid"] = driveId,
            ["sprk_graphitemid"] = itemId,
            ["_sprk_checkedoutby_value"] = null,
        };

        public void CheckOut(Guid id, string holder)
        {
            _rows[id]["_sprk_checkedoutby_value"] = Guid.NewGuid().ToString();
            _rows[id]["sprk_CheckedOutBy"] = new Dictionary<string, object?> { ["fullname"] = holder };
        }

        public bool Contains(Guid id) => _rows.ContainsKey(id);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var open = path.LastIndexOf('(');
            if (!path.Contains("/sprk_documents(", StringComparison.Ordinal) || open < 0
                || !Guid.TryParse(path[(open + 1)..path.LastIndexOf(')')], out var id))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));

            if (request.Method == HttpMethod.Delete)
            {
                if (RowDeleteStatus is { } status)
                    return Task.FromResult(new HttpResponseMessage(status));
                Steps.Enqueue($"row-delete:{id}");
                return Task.FromResult(new HttpResponseMessage(
                    _rows.TryRemove(id, out _) ? HttpStatusCode.NoContent : HttpStatusCode.NotFound));
            }

            return Task.FromResult(_rows.TryGetValue(id, out var row)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(row), System.Text.Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>The delete-own seams of <see cref="ExternalDataService"/> in memory, plus the requester read.</summary>
    public sealed class DeleteOwnDataService : ExternalDataService
    {
        public DeleteOwnDataService()
            : base(new HttpClient(), new ConfigurationBuilder().Build(),
                   new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential(),
                   NullLogger<ExternalDataService>.Instance)
        {
        }

        public ConcurrentDictionary<(DeletableKind Kind, Guid Id), DeleteTarget> Targets { get; } = new();

        public ConcurrentDictionary<Guid, Guid> Requesters { get; } = new();

        public Exception? TargetFault { get; set; }

        public Exception? DeleteFault { get; set; }

        public Exception? RequesterFault { get; set; }

        public void Reset()
        {
            Targets.Clear();
            Requesters.Clear();
            TargetFault = null;
            DeleteFault = null;
            RequesterFault = null;
        }

        public override Task<DeleteTarget?> GetDeleteTargetAsync(DeletableKind kind, Guid id, CancellationToken ct = default)
        {
            if (TargetFault is { } fault)
                return Task.FromException<DeleteTarget?>(fault);
            return Task.FromResult(Targets.TryGetValue((kind, id), out var target) ? target : null);
        }

        public override Task<bool> DeleteRecordAsync(DeletableKind kind, Guid id, CancellationToken ct = default)
        {
            if (kind == DeletableKind.Document)
                throw new InvalidOperationException("A document must be deleted through DocumentCheckoutService.");
            if (DeleteFault is { } fault)
                return Task.FromException<bool>(fault);
            return Task.FromResult(Targets.TryRemove((kind, id), out _));
        }

        public override Task<Guid?> GetServiceRequestRequesterAsync(Guid serviceRequestId, CancellationToken ct = default)
        {
            if (RequesterFault is { } fault)
                return Task.FromException<Guid?>(fault);
            return Task.FromResult(Requesters.TryGetValue(serviceRequestId, out var requester) ? requester : (Guid?)null);
        }
    }
}
