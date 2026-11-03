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
using Sprk.Bff.Api.Infrastructure.Dataverse;
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
/// <para>The service request delete (owner decision 2026-10-11, option A, plus recall) is tested in
/// <c>ExternalDeleteServiceRequestTests</c> on this fixture; this class pins that there are FOUR delete routes.</para>
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

    /// <summary>A record the caller holds nothing on (task 039 fix round).</summary>
    private static readonly Guid Hidden = Guid.Parse("a0320000-0000-4000-8000-0000000000dd");

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

    [Theory]
    [InlineData(DocumentParentKind.Project)]
    [InlineData(DocumentParentKind.Matter)]
    [InlineData(DocumentParentKind.WorkAssignment)]
    public async Task Document_AlsoFiledToARootTheCallerCannotRead_ThroughARelatedTwin_Is403(DocumentParentKind hidden)
    {
        // Task 039 fix round: the related twin of each root (sprk_relatedproject / _relatedmatter / _relatedworkassignment) is a
        // parent the delete consults, as the list, download and versions do. Seed models what ParseDeleteTarget reads from it.
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact,
            parents: new[] { Project(RecordA), new DeleteTargetParent(hidden, Hidden) });
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.FullAccess);

        using var response = await Delete("documents", id);

        await ShouldBeTheUniformDenial(response);
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
        _fixture.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task Document_AlsoFiledToATwinRootTheCallerCanRead_IsDeleted()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact,
            parents: new[] { Project(RecordA), new DeleteTargetParent(DocumentParentKind.Matter, RecordB) });
        _fixture.Principal = Contact(
            projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) },
            matters: new Dictionary<Guid, AccessRights> { [RecordB] = AccessRights.Read });

        using var response = await Delete("documents", id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.Exists(DeletableKind.Document, id).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task Row_WithAParentLookupThatIsNotAGuid_Is403_FailClosed(string segment)
    {
        var id = _fixture.Seed(KindOf(segment), contact: CallerContact, parents: Project(RecordA));
        _fixture.Data.Targets[(KindOf(segment), id)] =
            new DeleteTarget(null, CallerContact, new[] { Project(RecordA) }, HasUnreadableParentLink: true);
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.FullAccess);

        using var response = await Delete(segment, id);

        await ShouldBeTheUniformDenial(response);
        _fixture.Exists(KindOf(segment), id).Should().BeTrue();
        _fixture.Steps.Should().BeEmpty();
    }

    [Fact]
    public void ParseDeleteTarget_ReadsTheRelatedTwinsAsParents_AndFlagsAValueThatIsNotAGuid()
    {
        using var doc = JsonDocument.Parse($$"""
            {
              "_sprk_createdbycontact_value": "{{CallerContact}}",
              "_sprk_project_value": "{{RecordA}}",
              "_sprk_relatedmatter_value": "{{RecordB}}",
              "_sprk_relatedworkassignment_value": null,
              "_sprk_relatedproject_value": ""
            }
            """);
        var row = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

        var target = ParseDeleteTarget(DeletableKind.Document, row);

        target.HasUnreadableParentLink.Should().BeFalse("null and blank are 'not filed'");
        target.Parents.Should().BeEquivalentTo(new[] { Project(RecordA), new DeleteTargetParent(DocumentParentKind.Matter, RecordB) });

        foreach (var garbled in new[] { "\"not-a-guid\"", "42", "true" })
        {
            using var bad = JsonDocument.Parse($$"""{ "_sprk_relatedmatter_value": {{garbled}}, "_sprk_project_value": "{{RecordA}}" }""");
            ParseDeleteTarget(DeletableKind.Document, bad.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()))
                .HasUnreadableParentLink.Should().BeTrue($"{garbled} is present but not a GUID");
        }
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
    // Task 036 (FR-20, owner round 59 item 2): a caller whose access Dataverse decided ALSO needs Delete on the row in
    // Dataverse, asked as the caller. The creator stamp and the parent Read stay required.
    // =====================================================================

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task Fr20_TheCreator_WithoutDeleteInDataverse_Is403_AndNothingIsDeleted(string segment)
    {
        var id = _fixture.Seed(KindOf(segment), contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = DataverseDecidedSystemUserOn(RecordA, linkedContact: CallerContact);
        // Everything but Delete: Write and AppendTo on the row admit no delete.
        _fixture.Probe.AnswerAppOnly(CallerUser, DeletableSetOf(segment), id, AccessRights.Read | AccessRights.Write | AccessRights.AppendTo);

        using var response = await Delete(segment, id);

        await ShouldBeTheUniformDenial(response);
        _fixture.Exists(KindOf(segment), id).Should().BeTrue("nothing is deleted without Dataverse's Delete");
        _fixture.Probe.AppOnlyCalls.Should().ContainSingle().Which.Should().Match<(Guid SystemUserId, string EntitySet, Guid RecordId)>(
            c => c.SystemUserId == CallerUser && c.EntitySet == DeletableSetOf(segment) && c.RecordId == id, "Dataverse is asked about the ROW, not its parent");
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task Fr20_TheCreator_WithDeleteInDataverse_Deletes(string segment)
    {
        var id = _fixture.Seed(KindOf(segment), contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = DataverseDecidedSystemUserOn(RecordA, linkedContact: CallerContact);
        _fixture.Probe.AnswerAppOnly(CallerUser, DeletableSetOf(segment), id, AccessRights.Read | AccessRights.Delete);

        using var response = await Delete(segment, id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.Exists(KindOf(segment), id).Should().BeFalse();
        _fixture.Probe.AppOnlyCalls.Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task Fr20_DeleteInDataverse_NeverStandsInForTheCreatorStamp(string segment)
    {
        var id = _fixture.Seed(KindOf(segment), contact: OtherContact, parents: Project(RecordA));
        _fixture.Principal = DataverseDecidedSystemUserOn(RecordA, linkedContact: CallerContact);
        _fixture.Probe.AnswerAppOnly(CallerUser, DeletableSetOf(segment), id, AccessRights.Read | AccessRights.Delete);

        using var response = await Delete(segment, id);

        await ShouldBeTheUniformDenial(response);
        _fixture.Exists(KindOf(segment), id).Should().BeTrue();
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty("the stamp is checked first, and a row that is not the caller's asks nothing");
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task Fr20_AProbeThatFaults_Is503_AndNothingIsDeleted(string segment)
    {
        // Fix round 4 (F1): AuthorizeDeleteOwnAsync's own contract — a read that FAILED is the 503, never a 403.
        var id = _fixture.Seed(KindOf(segment), contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = DataverseDecidedSystemUserOn(RecordA, linkedContact: CallerContact);
        _fixture.Probe.AnswerAppOnly(CallerUser, DeletableSetOf(segment), id, AccessRights.Read | AccessRights.Delete);
        _fixture.Probe.AppOnlyFault = new HttpRequestException("Dataverse unreachable");

        using var response = await Delete(segment, id);

        await ShouldBeDeleteOwnUnavailable(response);
        _fixture.Exists(KindOf(segment), id).Should().BeTrue("a failed confirmation adds no rights (NFR-01)");
    }

    /// <summary>Task 036 fix round 4 (F1): every kind, against each way Dataverse can fail to answer.</summary>
    public static TheoryData<string, string> KindsByUnansweredShape()
    {
        var data = new TheoryData<string, string>();
        foreach (var segment in new[] { "documents", "todos", "events" })
        {
            data.Add(segment, CallerRecordAccessProbeDouble.WireShape.Timeout);
            data.Add(segment, CallerRecordAccessProbeDouble.WireShape.Unknown);
            data.Add(segment, CallerRecordAccessProbeDouble.WireShape.ServerFault);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(KindsByUnansweredShape))]
    public async Task Fr20_DataverseGivesNoAnswer_OverTheWire_Is503_AndNothingIsDeleted(string segment, string shape)
    {
        var id = _fixture.Seed(KindOf(segment), contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = DataverseDecidedSystemUserOn(RecordA, linkedContact: CallerContact);
        _fixture.Probe.UseAppOnlyWire(shape);

        using var response = await Delete(segment, id);

        await ShouldBeDeleteOwnUnavailable(response);
        _fixture.Exists(KindOf(segment), id).Should().BeTrue("nothing is deleted on a question Dataverse did not answer");
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task Fr20_ARealDenial_OverTheWire_StaysTheUniform403_AndNothingIsDeleted(string segment)
    {
        var id = _fixture.Seed(KindOf(segment), contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = DataverseDecidedSystemUserOn(RecordA, linkedContact: CallerContact);
        _fixture.Probe.UseAppOnlyWire(CallerRecordAccessProbeDouble.WireShape.Denial);

        using var response = await Delete(segment, id);

        await ShouldBeTheUniformDenial(response);
        _fixture.Exists(KindOf(segment), id).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task Fr20_FlagOff_TheSystemUserCreatorDeletes_AndDataverseIsNeverAsked(string segment)
    {
        var id = _fixture.Seed(KindOf(segment), contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = SystemUserOn(RecordA, linkedContact: CallerContact);

        using var response = await Delete(segment, id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.Exists(KindOf(segment), id).Should().BeFalse();
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty("a caller whose access Dataverse did not decide is decided exactly as before");
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
        _fixture.Steps.Should().Equal(new[] { $"row-delete:{id}" }, "an already-gone file is never touched");
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
    // Task 188 slice D: sprk_document_ParentDocument is Delete = Restrict. A document other documents name as their
    // parent is refused 409 BEFORE its file is touched; the link (which D7's narrowing walks) is kept.
    // =====================================================================

    [Fact]
    public async Task Document_WithChildDocuments_Is409_NamesNoCount_AndNothingIsDeleted()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        var child = _fixture.Dataverse.AddChild(id);
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);

        using var response = await Delete("documents", id);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        ProblemCode(body).Should().Be(DocumentCheckoutService.HasChildDocumentsCode);
        body.Should().NotContain("childCount", "a child the caller cannot see (D7) must not be counted to them")
            .And.NotContain(child.ToString());
        _fixture.Steps.Should().BeEmpty("the file is not touched: a Restrict refusal of the row would otherwise leave "
            + "a row whose file is gone");
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
        _fixture.Dataverse.Contains(child).Should().BeTrue();
    }

    [Fact]
    public async Task InternalDelete_ParentWithChildDocuments_IsRefused_WithTheCount_BeforeTheFile()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Dataverse.AddChild(id);
        _fixture.Dataverse.AddChild(id);
        var service = _fixture.Services.GetRequiredService<DocumentCheckoutService>();

        var result = await service.DeleteAsync(id, "corr-188d");

        result.Should().BeOfType<HasChildDocumentsDeleteResult>().Which.ChildCount.Should().Be(2);
        _fixture.Steps.Should().BeEmpty("the file is deleted before the row; the refusal must come first");
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
    }

    [Fact]
    public async Task Delete_OfAChildDocument_IsNotRefused()
    {
        // Must-not-fire: the rule is about documents that ARE a parent, not documents that HAVE one.
        var parent = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        var child = _fixture.Dataverse.AddChild(parent);
        var service = _fixture.Services.GetRequiredService<DocumentCheckoutService>();

        var result = await service.DeleteAsync(child, "corr-188d");

        result.Should().BeOfType<SuccessDeleteResult>();
        _fixture.Dataverse.Contains(child).Should().BeFalse();
        _fixture.Steps.Should().Equal($"file-delete:drive-{child:N}/item-{child:N}", $"row-delete:{child}");
    }

    [Fact]
    public async Task Document_WhenTheChildDocumentReadFails_Is503_AndNothingIsDeleted()
    {
        // Fail closed: "could not tell whether it has children" never proceeds to the file.
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);
        _fixture.Dataverse.ChildReadStatus = HttpStatusCode.ServiceUnavailable;

        using var response = await Delete("documents", id);

        await ShouldBeDeleteOwnUnavailable(response);
        _fixture.Steps.Should().BeEmpty();
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
    }

    // =====================================================================
    // ebilling-r1 task 094 (ontology-r1 088 gate F1; procedure-and-file-model 5.2): a Stored File copied from a document
    // names it as sprk_sourcedocument (Delete = Restrict). The document is refused with a 409 BEFORE its file is touched, never
    // a Restrict refusal after the file is gone. Fix round F1-c (owner EB-D-166: blocking is correct): its own code, not
    // "has child documents" - deleting other documents first would not help. The external plane is generic: "in use", no count.
    // =====================================================================

    [Fact(DisplayName = "094 F1-c (must fire): a document a Stored File was copied from is a generic 409 document_in_use on the external route - no count, no invoice; no file or row is deleted")]
    public async Task Document_WithAStoredFileCopy_Is409_InUse_AndNothingIsDeleted()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Dataverse.StoredFileCopies[id] = 1;
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);

        using var response = await Delete("documents", id);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        ProblemCode(body).Should().Be(DocumentCheckoutService.DocumentInUseCode);
        body.Should().NotContain("childCount", "the external plane never counts what refuses the delete");
        body.Should().NotContainEquivalentOf("invoice", "the external plane does not say what the document is in use by");
        body.Should().NotContainEquivalentOf("child", "it is not a parent of child documents");
        _fixture.Steps.Should().BeEmpty("the SPE file is untouched");
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
    }

    [Fact(DisplayName = "094 F1-c (must fire): the internal delete of a document a Stored File was copied from is refused as an invoice source (its own code), before the file")]
    public async Task InternalDelete_WithStoredFileCopies_IsRefused_AsAnInvoiceSource_BeforeTheFile()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Dataverse.StoredFileCopies[id] = 2;
        var service = _fixture.Services.GetRequiredService<DocumentCheckoutService>();

        var result = await service.DeleteAsync(id, "corr-094");

        result.Should().BeOfType<InvoiceSourceDeleteResult>();
        _fixture.Steps.Should().BeEmpty();
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
    }

    [Fact(DisplayName = "094 F1-c: a document with child documents AND Stored File copies is refused for its children first, with their count only")]
    public async Task InternalDelete_WithChildrenAndStoredFileCopies_IsRefusedForTheChildren_WithTheirCount()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Dataverse.AddChild(id);
        _fixture.Dataverse.StoredFileCopies[id] = 2;
        var service = _fixture.Services.GetRequiredService<DocumentCheckoutService>();

        var result = await service.DeleteAsync(id, "corr-094");

        var refused = result.Should().BeOfType<HasChildDocumentsDeleteResult>().Subject;
        refused.ChildCount.Should().Be(1, "the Stored File copies are not child documents");
        refused.FileGone.Should().BeFalse();
        _fixture.Steps.Should().BeEmpty();
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
    }

    [Fact(DisplayName = "094 (must not fire): a document no Stored File names is asked about and deletes as before, file then row")]
    public async Task InternalDelete_WithoutStoredFileCopies_Deletes()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Dataverse.StoredFileCopies[Guid.NewGuid()] = 1;
        var service = _fixture.Services.GetRequiredService<DocumentCheckoutService>();

        var result = await service.DeleteAsync(id, "corr-094");

        result.Should().BeOfType<SuccessDeleteResult>();
        _fixture.Dataverse.StoredFileQueries.Should().Contain(id);
        _fixture.Steps.Should().Equal($"file-delete:drive-{id:N}/item-{id:N}", $"row-delete:{id}");
        _fixture.Exists(DeletableKind.Document, id).Should().BeFalse();
    }

    [Fact(DisplayName = "094 (fail closed): when the Stored File copies cannot be read the delete is 503 and nothing is deleted")]
    public async Task Document_WhenTheStoredFileCopyReadFails_Is503_AndNothingIsDeleted()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);
        _fixture.Dataverse.StoredFileReadStatus = HttpStatusCode.ServiceUnavailable;

        using var response = await Delete("documents", id);

        await ShouldBeDeleteOwnUnavailable(response);
        _fixture.Steps.Should().BeEmpty();
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
    }

    // =====================================================================
    // Task 188 slice D, security pass 2: a child created AFTER the count (the email pipeline writing attachments) makes
    // Dataverse's Restrict refuse the row once the file is gone. The refusal maps to the same 409, never a 500.
    // =====================================================================

    [Fact]
    public async Task Document_WhenAChildAppearsAfterTheCount_TheRestrictRefusal_Is409_NotA500_AndTheRowIsKept()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);
        Guid? child = null;
        _fixture.DuringFileDelete = () => child = _fixture.Dataverse.AddChild(id);

        using var response = await Delete("documents", id);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        ProblemCode(body).Should().Be(DocumentCheckoutService.HasChildDocumentsCode);
        body.Should().NotContain("childCount", "the external plane never counts children")
            .And.Contain("file was deleted", "the detail must not claim nothing was deleted");
        _fixture.Steps.Should().Equal(new[] { $"file-delete:drive-{id:N}/item-{id:N}" },
            "the file goes first (by design) and the row delete is refused");
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue("Restrict kept the row, and with it the link");
        _fixture.Dataverse.Contains(child!.Value).Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task InternalDelete_WhenAChildAppearsAfterTheCount_IsHasChildDocuments_WithTheFileGone(HttpStatusCode restrictStatus)
    {
        // Matched on Dataverse's error code, whatever HTTP status carries it.
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Dataverse.RestrictStatus = restrictStatus;
        _fixture.DuringFileDelete = () => _fixture.Dataverse.AddChild(id);
        var service = _fixture.Services.GetRequiredService<DocumentCheckoutService>();

        var result = await service.DeleteAsync(id, "corr-188d-race");

        var refused = result.Should().BeOfType<HasChildDocumentsDeleteResult>().Subject;
        refused.ChildCount.Should().BeNull("Dataverse's refusal names no count");
        refused.FileGone.Should().BeTrue();
        _fixture.Steps.Should().Equal($"file-delete:drive-{id:N}/item-{id:N}");
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
    }

    [Fact]
    public async Task InternalDelete_Retry_AfterTheChildrenAreDeleted_DeletesTheRowWhoseFileIsGone()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        Guid? child = null;
        _fixture.DuringFileDelete = () => child = _fixture.Dataverse.AddChild(id);
        var service = _fixture.Services.GetRequiredService<DocumentCheckoutService>();
        (await service.DeleteAsync(id, "corr-188d-race")).Should().BeOfType<HasChildDocumentsDeleteResult>();

        // The attachment is deleted, and the parent's file is now missing in SharePoint Embedded.
        _fixture.DuringFileDelete = null;
        _fixture.Dataverse.Remove(child!.Value);
        _fixture.FileAlreadyGone = true;

        var retry = await service.DeleteAsync(id, "corr-188d-retry");

        retry.Should().BeOfType<SuccessDeleteResult>("a missing file counts as deleted, so the row may go");
        _fixture.Exists(DeletableKind.Document, id).Should().BeFalse();
        // R3 ISS-004 review F1: the retry meets the pointer check's refusal of the item Graph no longer has; the row's bound
        // item id and its owned drive prove the file is gone, so the row goes and NO second file delete is attempted.
        _fixture.Steps.Should().Equal(
            $"file-delete:drive-{id:N}/item-{id:N}",
            $"row-delete:{id}");
    }

    [Fact]
    public async Task Document_WhenTheRowDeleteFailsWithAnotherDataverseCode_IsStill500()
    {
        // Must-not-fire: only 0x80040227 is the Restrict refusal.
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);
        _fixture.Dataverse.RowDeleteStatus = HttpStatusCode.Forbidden;
        _fixture.Dataverse.RowDeleteErrorCode = "0x80040220";

        using var response = await Delete("documents", id);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        ProblemCode(await response.Content.ReadAsStringAsync()).Should().Be("document_row_not_deleted");
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
        ProblemCode(await response.Content.ReadAsStringAsync()).Should().Be("sdap.access.delete_own_unavailable");
        _fixture.Exists(KindOf(segment), id).Should().BeTrue();
        _fixture.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task Document_WhenTheRowReadTimesOut_Is503_AndNothingIsDeleted()
    {
        // HttpClient.Timeout surfaces as a TaskCanceledException (inner TimeoutException) while the request token is NOT
        // cancelled. It is a failed read, not the caller leaving: 503, not a bare 500.
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);
        _fixture.Dataverse.ReadFault = new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());

        using var response = await Delete("documents", id);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        ProblemCode(await response.Content.ReadAsStringAsync()).Should().Be("sdap.access.delete_own_unavailable");
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
        _fixture.Steps.Should().BeEmpty("nothing is deleted when the document row could not be read");
    }

    // Task 187, fix round 1, owner decision D3: a document's delete-own runs the same live-parent check as a to-do's and an
    // event's: behind a secure root's wall it is the uniform 403, a read that could not decide is the 503, and nothing is
    // deleted (neither the SPE file nor the row).

    [Theory]
    [InlineData(LiveParentDecision.Withheld, HttpStatusCode.Forbidden, "sdap.access.deny.delete_own")]
    [InlineData(LiveParentDecision.Unavailable, HttpStatusCode.ServiceUnavailable, "sdap.access.delete_own_unavailable")]
    public async Task Document_WhoseLiveParentCheckRefuses_IsRefused_AndNothingIsDeleted(
        LiveParentDecision decision, HttpStatusCode expected, string reasonCode)
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);
        _fixture.Data.LiveParents[(DeletableKind.Document, id)] = decision;

        using var response = await Delete("documents", id);

        response.StatusCode.Should().Be(expected);
        ProblemCode(await response.Content.ReadAsStringAsync()).Should().Be(reasonCode);
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
        _fixture.Steps.Should().BeEmpty("neither the file nor the row is deleted");
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
    // AC 7: no DELETE route for communications (messages); the service request route is the fourth (owner decision 2026-10-11)
    // =====================================================================

    [Fact]
    public void TheExternalPlane_MapsExactlyFourDeleteRoutes()
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
            "/api/v1/external/events/{id:guid}",
            "/api/v1/external/servicerequests/{id:guid}");
    }

    [Theory]
    [InlineData("messages")]
    [InlineData("communications")]
    public async Task NoDeleteRoute_ForMessages(string segment)
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
        "_sprk_createdbyperson_value,_sprk_createdbycontact_value,_sprk_project_value,_sprk_matter_value,_sprk_workassignment_value,_sprk_relatedservicerequest_value,_sprk_invoice_value,_sprk_relatedproject_value,_sprk_relatedmatter_value,_sprk_relatedworkassignment_value")]
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

    // R3 ISS-004 (#1650, owner decision 2026-10-22): the internal DELETE /api/documents/{id} (the three-argument
    // DeleteAsync) uses the SAME policy as the external route, VerifiedFileFirstKeepRowOnFailure. It replaces the
    // task 032 test InternalDelete_BestEffort_IsUnchanged_AFileFaultStillDeletesTheRow, which pinned the old behaviour
    // (a failed file delete was logged and the row deleted anyway, orphaning the file).

    [Fact]
    public async Task InternalDelete_WhenTheFileDeleteFails_KeepsTheRow_AndAnswers503()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.FileFault = new InvalidOperationException("Graph said 503");
        var service = _fixture.Services.GetRequiredService<DocumentCheckoutService>();

        var result = await service.DeleteAsync(id, "corr-iss004");

        var kept = result.Should().BeOfType<FileNotDeletedDeleteResult>().Subject;
        kept.StatusCode.Should().Be(503);
        kept.Code.Should().Be(DocumentCheckoutService.FileNotDeletedCode);
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue("the row is kept when its file was not deleted");
        _fixture.Steps.Should().Equal($"file-delete:drive-{id:N}/item-{id:N}");
    }

    [Fact]
    public async Task InternalDelete_WhenTheFileDeletes_DeletesTheFileThenTheRow()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        var service = _fixture.Services.GetRequiredService<DocumentCheckoutService>();

        var result = await service.DeleteAsync(id, "corr-iss004");

        result.Should().BeOfType<SuccessDeleteResult>();
        _fixture.Exists(DeletableKind.Document, id).Should().BeFalse();
        _fixture.Steps.Should().Equal($"file-delete:drive-{id:N}/item-{id:N}", $"row-delete:{id}");
    }

    [Fact]
    public async Task InternalDelete_WhenTheFileIsAlreadyGone_StillDeletesTheRow()
    {
        // "File not found" is success: the issue's suggested fix, and the retry that clears an earlier 503. The fixture marks
        // the item missing in the pointer check's Graph world too (review F3), so the check meets the refusal a real
        // already-deleted file meets; the bound item id and the owned drive let the row go (review F1).
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.FileAlreadyGone = true;
        var service = _fixture.Services.GetRequiredService<DocumentCheckoutService>();

        var result = await service.DeleteAsync(id, "corr-iss004");

        result.Should().BeOfType<SuccessDeleteResult>();
        _fixture.Exists(DeletableKind.Document, id).Should().BeFalse();
        _fixture.Steps.Should().Equal(new[] { $"row-delete:{id}" }, "an already-gone file is never touched");
    }

    [Fact]
    public async Task InternalDelete_WhosePointerIsRefused_KeepsTheRow_And409_AndTheFileIsNotTouched()
    {
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA),
            driveId: "b!foreign-container");
        var service = _fixture.Services.GetRequiredService<DocumentCheckoutService>();

        var result = await service.DeleteAsync(id, "corr-iss004");

        result.Should().BeOfType<FileNotDeletedDeleteResult>().Which.StatusCode.Should().Be(409);
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
        _fixture.Steps.Should().BeEmpty("a refused pointer is never followed");
    }

    [Fact]
    public async Task InternalDelete_WithBestEffortPolicy_StillDeletesTheRowAfterAFileFault()
    {
        // The BestEffort policy is retained (it has no production caller after ISS-004); it behaves as before.
        var id = _fixture.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));
        _fixture.FileFault = new InvalidOperationException("Graph said 503");
        var service = _fixture.Services.GetRequiredService<DocumentCheckoutService>();

        var result = await service.DeleteAsync(id, "corr-iss004", DocumentFileDeletePolicy.BestEffort);

        result.Should().BeOfType<SuccessDeleteResult>();
        _fixture.Exists(DeletableKind.Document, id).Should().BeFalse();
    }

    // =====================================================================
    // R3 ISS-004 review F1 / F3: the pointer check on a DELETE, run on BOTH document-pointer rules.
    //
    // The delete's pointer check refuses an item Graph no longer has (the interim and strict rules both do, right for a
    // download), which used to make "the file is already gone" a permanent 409 and a faulted check a permanent 409 too.
    // The REAL check runs over a world where FileAlreadyGone also marks the item missing, and rows carry their bound item id.
    // One contract, two fixtures: the interim rule, and the strict rule with the backfill complete (what dev runs).
    // =====================================================================

    /// <summary>The delete-path pointer-check contract; the two nested classes below run it per rule.</summary>
    [Trait("category", "external-access")]
    public abstract class PointerCheckOnDeleteContract
    {
        protected readonly Fixture _f;

        protected PointerCheckOnDeleteContract(Fixture fixture)
        {
            _f = fixture;
            _f.Reset();
        }

        protected DocumentCheckoutService Service => _f.Services.GetRequiredService<DocumentCheckoutService>();

        protected Guid SeedDocument() =>
            _f.Seed(DeletableKind.Document, contact: CallerContact, parents: Project(RecordA));

        private async Task<HttpResponseMessage> DeleteOverTheExternalRoute(Guid id)
        {
            _f.Principal = ContactOn(RecordA, ExternalAccessLevel.Collaborate);
            using var client = _f.CreateAuthenticatedClient();
            return await client.DeleteAsync($"/api/v1/external/documents/{id}");
        }

        // ---- a file that is already gone ----

        [Fact]
        public async Task AlreadyGone_BoundPointer_OwnedContainer_DeletesTheRow_AndTouchesNoFile()
        {
            var id = SeedDocument();
            _f.FileAlreadyGone = true;

            var result = await Service.DeleteAsync(id, "corr-f1");

            result.Should().BeOfType<SuccessDeleteResult>();
            _f.Exists(DeletableKind.Document, id).Should().BeFalse();
            _f.Steps.Should().Equal(new[] { $"row-delete:{id}" }, "an already-gone file is never touched");
        }

        [Fact]
        public async Task ExternalRoute_AlreadyGone_BoundPointer_OwnedContainer_Is204_AndTouchesNoFile()
        {
            // The external delete-own route calls the same DeleteAsync, so the fix reaches the task 032 known limit too.
            var id = SeedDocument();
            _f.FileAlreadyGone = true;

            using var response = await DeleteOverTheExternalRoute(id);

            response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
            _f.Exists(DeletableKind.Document, id).Should().BeFalse();
            _f.Steps.Should().Equal(new[] { $"row-delete:{id}" });
        }

        [Fact]
        public async Task AlreadyGone_BoundToAnotherItem_KeepsTheRow_409()
        {
            var id = SeedDocument();
            _f.RebindElsewhere(id);
            _f.FileAlreadyGone = true;

            var result = await Service.DeleteAsync(id, "corr-f1");

            var kept = result.Should().BeOfType<FileNotDeletedDeleteResult>().Subject;
            kept.StatusCode.Should().Be(409);
            kept.Code.Should().Be(RecordContainerResolver.DocumentStorageUnverifiedCode);
            _f.Exists(DeletableKind.Document, id).Should().BeTrue();
            _f.Steps.Should().BeEmpty();
        }

        [Fact]
        public async Task AlreadyGone_UnboundPointer_KeepsTheRow_409()
        {
            // An empty copy proves nothing about who set the pointer: refused on both rules, even with the item gone.
            var id = SeedDocument();
            _f.Unbind(id);
            _f.FileAlreadyGone = true;

            var result = await Service.DeleteAsync(id, "corr-f1");

            result.Should().BeOfType<FileNotDeletedDeleteResult>().Which.StatusCode.Should().Be(409);
            _f.Exists(DeletableKind.Document, id).Should().BeTrue();
            _f.Steps.Should().BeEmpty();
        }

        [Fact]
        public async Task AlreadyGone_ContainerThisStampDoesNotOwn_KeepsTheRow_409()
        {
            var id = SeedDocument();
            _f.PointerWorld.UnownedDrives.Add(_f.DriveOf(id));
            _f.FileAlreadyGone = true;

            var result = await Service.DeleteAsync(id, "corr-f1");

            result.Should().BeOfType<FileNotDeletedDeleteResult>().Which.StatusCode.Should().Be(409,
                "another customer's container is refused, not read as 'the file is gone'");
            _f.Exists(DeletableKind.Document, id).Should().BeTrue();
            _f.Steps.Should().BeEmpty();
        }

        [Fact]
        public async Task ExternalRoute_AlreadyGone_ButBoundToAnotherItem_Is409_AndTheRowIsKept()
        {
            var id = SeedDocument();
            _f.RebindElsewhere(id);
            _f.FileAlreadyGone = true;

            using var response = await DeleteOverTheExternalRoute(id);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
            _f.Exists(DeletableKind.Document, id).Should().BeTrue();
            _f.Steps.Should().BeEmpty();
        }

        [Fact]
        public async Task ItemStillThere_ButPointerRefused_IsNeverTreatedAsGone()
        {
            // Must-not-fire: only a 404 from Graph reads as 'gone'. An existing item in a container the check refuses stays
            // refused even though the row's bound id matches.
            var id = _f.Seed(DeletableKind.Document, CallerContact, Project(RecordA), "b!foreign-container");

            var result = await Service.DeleteAsync(id, "corr-f1");

            result.Should().BeOfType<FileNotDeletedDeleteResult>().Which.StatusCode.Should().Be(409);
            _f.Exists(DeletableKind.Document, id).Should().BeTrue();
            _f.Steps.Should().BeEmpty("a refused pointer is never followed");
        }

        // ---- a pointer check that faults ----

        public static TheoryData<string> CheckFaults => new() { "graph", "dataverse" };

        private void ScriptCheckFault(string kind)
        {
            if (kind == "graph")
            {
                _f.PointerWorld.ItemReadFault = new HttpRequestException("Graph 429 Too Many Requests");
            }
            else
            {
                _f.PointerWorld.RetrieveFault = (entity, _) =>
                    entity == "sprk_document" ? new TimeoutException("Dataverse timed out") : null;
            }
        }

        [Theory]
        [MemberData(nameof(CheckFaults))]
        public async Task PointerCheckFault_IsRetryable503_NotA409_AndNoFileIsTouched(string kind)
        {
            var id = SeedDocument();
            ScriptCheckFault(kind);

            var result = await Service.DeleteAsync(id, "corr-f1");

            var kept = result.Should().BeOfType<FileNotDeletedDeleteResult>().Subject;
            kept.StatusCode.Should().Be(503);
            kept.Code.Should().Be(DocumentCheckoutService.FileNotDeletedCode);
            _f.Exists(DeletableKind.Document, id).Should().BeTrue();
            _f.Steps.Should().BeEmpty("a check that could not be made never reaches the file");
        }

        [Theory]
        [MemberData(nameof(CheckFaults))]
        public async Task ExternalRoute_PointerCheckFault_Is503_WithTheFileNotDeletedCode(string kind)
        {
            var id = SeedDocument();
            ScriptCheckFault(kind);

            using var response = await DeleteOverTheExternalRoute(id);

            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
            ProblemCode(body).Should().Be(DocumentCheckoutService.FileNotDeletedCode);
            _f.Exists(DeletableKind.Document, id).Should().BeTrue();
            _f.Steps.Should().BeEmpty();
        }

        [Fact]
        public async Task AFaultReadingTheBoundItemId_Is503_NotA409()
        {
            // The one-column read of the row's bound item id: the already-gone question on the interim rule (item gone,
            // rule refused, then this read), the rule's own read on the strict rule. A fault is a 503 either way.
            var id = SeedDocument();
            _f.FileAlreadyGone = true;
            _f.PointerWorld.RetrieveFault = (entity, columns) =>
                entity == "sprk_document" && columns.Length == 1 && columns[0] == DocumentPointerBinding.BoundItemIdColumn
                    ? new TimeoutException("Dataverse timed out")
                    : null;

            var result = await Service.DeleteAsync(id, "corr-f1");

            result.Should().BeOfType<FileNotDeletedDeleteResult>().Which.StatusCode.Should().Be(503);
            _f.Exists(DeletableKind.Document, id).Should().BeTrue();
            _f.Steps.Should().BeEmpty();
        }

        // ---- a Graph answer on the item read: permanent (409) or transient (503) — R3 ISS-004 re-check F3-2 ----

        [Theory]
        [InlineData(400, 409)]
        [InlineData(403, 409)]
        [InlineData(429, 503)]
        [InlineData(503, 503)]
        public async Task GraphStatusOnTheItemRead_IsPermanent409Only_ForA400OrA403(int graphStatus, int expected)
        {
            var id = SeedDocument();
            _f.PointerWorld.ItemReadFault = new Microsoft.Graph.Models.ODataErrors.ODataError { ResponseStatusCode = graphStatus };

            var result = await Service.DeleteAsync(id, "corr-f3-2");

            var kept = result.Should().BeOfType<FileNotDeletedDeleteResult>().Subject;
            kept.StatusCode.Should().Be(expected, "a 400/403 is final for this pointer; a 429/5xx is worth retrying");
            kept.Code.Should().Be(expected == 409
                ? RecordContainerResolver.DocumentStorageUnverifiedCode
                : DocumentCheckoutService.FileNotDeletedCode);
            _f.Exists(DeletableKind.Document, id).Should().BeTrue();
            _f.Steps.Should().BeEmpty("a pointer that is refused or could not be checked never reaches the file");
        }

        // ---- the ordinary outcomes, unchanged ----

        [Fact]
        public async Task FileThere_DeletesTheFile_ThenTheRow()
        {
            var id = SeedDocument();

            var result = await Service.DeleteAsync(id, "corr-f1");

            result.Should().BeOfType<SuccessDeleteResult>();
            _f.Exists(DeletableKind.Document, id).Should().BeFalse();
            _f.Steps.Should().Equal(_f.FileStep(id), $"row-delete:{id}");
        }

        [Fact]
        public async Task GraphDeleteFault_KeepsTheRow_503()
        {
            var id = SeedDocument();
            _f.FileFault = new InvalidOperationException("Graph said 503");

            var result = await Service.DeleteAsync(id, "corr-f1");

            result.Should().BeOfType<FileNotDeletedDeleteResult>().Which.StatusCode.Should().Be(503);
            _f.Exists(DeletableKind.Document, id).Should().BeTrue();
            _f.Steps.Should().Equal(_f.FileStep(id));
        }

        [Fact]
        public async Task GraphDeleteTimedOutAfterDeleting_503_ThenTheRetryDeletesTheRow()
        {
            // The case that was a permanent 409: Graph deleted the file but the answer was lost, so the first attempt is a
            // 503; by the retry the item is gone and the pointer check would refuse it.
            var id = SeedDocument();
            _f.FileFault = new TimeoutException("Graph timed out");
            (await Service.DeleteAsync(id, "corr-f1")).Should().BeOfType<FileNotDeletedDeleteResult>()
                .Which.StatusCode.Should().Be(503);

            _f.FileFault = null;
            _f.FileAlreadyGone = true;
            var retry = await Service.DeleteAsync(id, "corr-f1-retry");

            retry.Should().BeOfType<SuccessDeleteResult>();
            _f.Exists(DeletableKind.Document, id).Should().BeFalse();
            _f.Steps.Should().Equal(_f.FileStep(id), $"row-delete:{id}");
        }

        [Fact]
        public async Task Retry_AfterTheChildrenAreDeleted_DeletesTheRowWhoseFileIsGone()
        {
            // Task 188 slice D: the file was deleted, a child appeared, the row was kept. The retry must succeed.
            var id = SeedDocument();
            Guid? child = null;
            _f.DuringFileDelete = () => child = _f.Dataverse.AddChild(id);
            (await Service.DeleteAsync(id, "corr-188d-race")).Should().BeOfType<HasChildDocumentsDeleteResult>();

            _f.DuringFileDelete = null;
            _f.Dataverse.Remove(child!.Value);
            _f.FileAlreadyGone = true;
            var retry = await Service.DeleteAsync(id, "corr-188d-retry");

            retry.Should().BeOfType<SuccessDeleteResult>("a missing file counts as deleted, so the row may go");
            _f.Exists(DeletableKind.Document, id).Should().BeFalse();
            _f.Steps.Should().Equal(_f.FileStep(id), $"row-delete:{id}");
        }
    }

    /// <summary>The contract under the INTERIM rule (the resolver's default).</summary>
    public sealed class PointerCheckOnDelete_InterimRule : PointerCheckOnDeleteContract, IClassFixture<Fixture>
    {
        public PointerCheckOnDelete_InterimRule(Fixture fixture) : base(fixture)
        {
        }
    }

    /// <summary>The contract under the STRICT derived-container rule with the item-id backfill complete (what dev runs).</summary>
    public sealed class PointerCheckOnDelete_StrictRule : PointerCheckOnDeleteContract, IClassFixture<StrictFixture>
    {
        public PointerCheckOnDelete_StrictRule(StrictFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task AFaultOnlyInTheDerivationRead_IsRetryable503_NotA409_AndTheRowIsKept()
        {
            // R3 ISS-004 re-check F3-1: the strict rule's binding read (one column) succeeds; the WIDE sprk_document read of
            // the derivation (the row's record links) faults. Folded into 'cannot be derived' it was a permanent 409.
            var id = SeedDocument();
            _f.PointerWorld.RetrieveFault = (entity, columns) =>
                entity == "sprk_document" && columns.Length > 1 ? new TimeoutException("Dataverse timed out") : null;

            var result = await Service.DeleteAsync(id, "corr-f3-1");

            var kept = result.Should().BeOfType<FileNotDeletedDeleteResult>().Subject;
            kept.StatusCode.Should().Be(503);
            kept.Code.Should().Be(DocumentCheckoutService.FileNotDeletedCode);
            _f.Exists(DeletableKind.Document, id).Should().BeTrue();
            _f.Steps.Should().BeEmpty("a check that could not be made never reaches the file");
        }
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
            SprkCreatedbypersonValue = CallerUser.ToString(),
            SprkCreatedbycontactValue = CallerContact.ToString(),
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

    private static async Task ShouldBeDeleteOwnUnavailable(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
        ProblemCode(body).Should().Be("sdap.access.delete_own_unavailable");
        body.Should().Contain("nothing was deleted").And.Contain("Try again");
    }

    private static async Task ShouldBeTheUniformDenial(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
        ProblemCode(body).Should().Be("sdap.access.deny.delete_own");
    }

    private static string? ReasonCode(string body) =>
        JsonDocument.Parse(body).RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;

    /// <summary>
    /// The code of a delete-route problem body. Every problem body on these routes carries it in <c>reasonCode</c>; the
    /// older <c>code</c> and the message routes' <c>errorCode</c> must be absent, so the SPA reads one key.
    /// </summary>
    private static string? ProblemCode(string body)
    {
        var root = JsonDocument.Parse(body).RootElement;
        root.TryGetProperty("code", out _).Should().BeFalse("the delete routes use one problem key: reasonCode");
        root.TryGetProperty("errorCode", out _).Should().BeFalse("the delete routes use one problem key: reasonCode");
        return ReasonCode(body);
    }

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

    /// <summary>Task 036: a systemuser composed with the FR-20 flag on — Dataverse decided its access, at Read only.</summary>
    private static CallerPrincipal DataverseDecidedSystemUserOn(Guid project, Guid linkedContact) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        SystemUserId = CallerUser,
        ContactId = linkedContact,
        Email = "licensed@customer.test",
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(project, ExternalAccessLevel.ViewOnly) },
        DataverseConfirmsWrites = true,
    };

    private static string DeletableSetOf(string segment) => ExternalDataService.GetDeletableBinding(KindOf(segment)).EntitySet;

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
    public class Fixture : ExternalCollaborationTestFixture
    {
        /// <summary>The one container every document of the STRICT-rule fixture lives in (the root unit's stamped container).</summary>
        public const string StrictContainer = "b!strict-root-container";

        private readonly ExternalTodoScopeTestFixture.StubCallerPrincipalResolver _principals = new();
        private readonly List<(string Drive, string Item)> _seededItems = new();
        private bool _fileAlreadyGone;

        /// <summary>The default fixture runs the INTERIM document-pointer rule (the resolver's default).</summary>
        public Fixture() : this(strictRule: false)
        {
        }

        /// <summary>
        /// <paramref name="strictRule"/>: the STRICT derived-container rule with the item-id backfill complete, the
        /// configuration dev runs (<c>DocumentPointer__StrictDerivedContainer</c>, <c>DocumentPointer__ItemIdBoundBackfillComplete</c>).
        /// </summary>
        protected Fixture(bool strictRule)
        {
            StrictRule = strictRule;
            PointerWorld = strictRule
                ? new TestRecordContainerResolver.DocumentPointerWorld { Strict = true, ItemIdBoundBackfillComplete = true }
                : new TestRecordContainerResolver.DocumentPointerWorld { RootClaims = c => c.StartsWith("drive-", StringComparison.Ordinal) };
            if (strictRule)
            {
                // The strict rule derives an UNFILED document's container from its owning business unit: the root's.
                PointerWorld.BusinessUnits[TestRecordContainerResolver.PointerWorldRootBusinessUnit] = (null, StrictContainer);
            }
        }

        /// <summary>The STRICT rule (with the backfill complete) is in force; otherwise the interim rule.</summary>
        public bool StrictRule { get; }

        /// <summary>
        /// The Dataverse and Graph world the REAL pointer check reads: a document row per seeded document (carrying its bound
        /// item id, as every BFF-written row does) and the drive items. A test scripts it (a missing item, an unowned drive,
        /// a fault); <see cref="Reset"/> restores it.
        /// </summary>
        internal TestRecordContainerResolver.DocumentPointerWorld PointerWorld { get; }

        /// <summary>The drive a document seeded by <see cref="Seed(DeletableKind, Guid?, Guid?, DeleteTargetParent[])"/> points at.</summary>
        public string DriveOf(Guid id) => StrictRule ? StrictContainer : $"drive-{id:N}";

        /// <summary>The <see cref="Steps"/> entry of the SPE file delete of a seeded document.</summary>
        public string FileStep(Guid id) => $"file-delete:{DriveOf(id)}/item-{id:N}";

        public DeleteOwnDataService Data { get; } = new();

        public DocumentRowsHandler Dataverse { get; } = new();

        /// <summary>Every destructive step, in order: <c>file-delete:{drive}/{item}</c> and <c>row-delete:{id}</c>.</summary>
        public ConcurrentQueue<string> Steps { get; } = new();

        /// <summary>Set to make <see cref="SpeFileStore.DeleteFileAsync"/> throw (Graph unavailable).</summary>
        public Exception? FileFault { get; set; }

        /// <summary>
        /// Set to make the file ALREADY GONE from SharePoint Embedded: <see cref="SpeFileStore.DeleteFileAsync"/> reports it
        /// not found, AND the pointer check's Graph item read (<see cref="PointerWorld"/>) answers 404 for every seeded
        /// document's item, as Graph would. Scripting only the delete (R3 ISS-004 review F3) left the check seeing the item
        /// as present, so the tests never met the refusal a real already-deleted file meets.
        /// </summary>
        public bool FileAlreadyGone
        {
            get => _fileAlreadyGone;
            set
            {
                _fileAlreadyGone = value;
                if (value)
                {
                    foreach (var key in _seededItems)
                        PointerWorld.Items[key] = null;
                }
            }
        }

        /// <summary>
        /// Runs inside <see cref="SpeFileStore.DeleteFileAsync"/>, i.e. after the child-document count and before the row
        /// delete (task 188 slice D): the race window in which the email pipeline creates an attachment.
        /// </summary>
        public Action? DuringFileDelete { get; set; }

        public CallerPrincipal? Principal
        {
            get => _principals.Principal;
            set => _principals.Principal = value;
        }

        /// <summary>Task 036: the caller's own Dataverse rights, as delete-own asks a Dataverse-decided caller for them.</summary>
        public CallerRecordAccessProbeDouble Probe { get; } = new();

        public void Reset()
        {
            Data.Reset();
            Dataverse.Reset();
            Probe.Reset();
            Steps.Clear();
            Principal = null;
            FileFault = null;
            _fileAlreadyGone = false;
            DuringFileDelete = null;
            PointerWorld.Rows.Clear();
            PointerWorld.Items.Clear();
            PointerWorld.UnownedDrives.Clear();
            PointerWorld.ItemReadFault = null;
            PointerWorld.RetrieveFault = null;
            _seededItems.Clear();
        }

        /// <summary>Seeds a deletable row with the given stamps and parents; a document also gets a row and a file.</summary>
        public Guid Seed(DeletableKind kind, Guid? person = null, Guid? contact = null,
            params DeleteTargetParent[] parents)
        {
            var id = Guid.NewGuid();
            Data.Targets[(kind, id)] = new DeleteTarget(person, contact, parents);
            if (kind == DeletableKind.Document)
                AddDocument(id, DriveOf(id));
            return id;
        }

        /// <summary><see cref="Seed(DeletableKind, Guid?, Guid?, DeleteTargetParent[])"/> with a chosen drive.</summary>
        public Guid Seed(DeletableKind kind, Guid? contact, DeleteTargetParent parents, string driveId)
        {
            var id = Guid.NewGuid();
            Data.Targets[(kind, id)] = new DeleteTarget(null, contact, new[] { parents });
            AddDocument(id, driveId);
            return id;
        }

        /// <summary>
        /// The document's Dataverse row (<see cref="Dataverse"/>) and the pointer check's view of it (<see cref="PointerWorld"/>):
        /// the field-secured item-id copy equals the pointer's item, as it does on every row the BFF wrote.
        /// </summary>
        private void AddDocument(Guid id, string driveId)
        {
            var item = $"item-{id:N}";
            Dataverse.Add(id, driveId, item);
            var row = TestRecordContainerResolver.DocumentPointerWorld.Document(id);
            row[DocumentPointerBinding.BoundItemIdColumn] = item;
            PointerWorld.Rows[("sprk_document", id)] = row;
            _seededItems.Add((driveId, item));
            if (_fileAlreadyGone)
                PointerWorld.Items[(driveId, item)] = null;
        }

        /// <summary>The row's field-secured item-id copy is EMPTY: the pointer was never bound (or written outside the BFF).</summary>
        public void Unbind(Guid id) => PointerWorld.Rows[("sprk_document", id)].Attributes.Remove(DocumentPointerBinding.BoundItemIdColumn);

        /// <summary>The row's field-secured item-id copy names ANOTHER item: the pointer was re-pointed outside the BFF.</summary>
        public void RebindElsewhere(Guid id) => PointerWorld.Rows[("sprk_document", id)][DocumentPointerBinding.BoundItemIdColumn] = "item-elsewhere";

        public bool Exists(DeletableKind kind, Guid id) =>
            kind == DeletableKind.Document ? Dataverse.Contains(id) : Data.Targets.ContainsKey((kind, id));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ICallerPrincipalResolver>(_ => _principals);
                services.AddScoped(_ => (ExternalDataService)Data);
                services.AddScoped<CallerRecordAccessProbe>(_ => Probe); // task 036 (overrides the typed client)

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
                    PointerWorld.Build()));
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
                _fixture.DuringFileDelete?.Invoke();
                if (_fixture.FileFault is { } fault)
                    return Task.FromException<bool>(fault);
                return Task.FromResult(!_fixture.FileAlreadyGone);
            }
        }
    }

    /// <summary>
    /// <see cref="Fixture"/> under the STRICT derived-container rule with the item-id backfill complete (what dev runs):
    /// every document lives in <see cref="Fixture.StrictContainer"/>, the container derived for it.
    /// </summary>
    public sealed class StrictFixture : Fixture
    {
        public StrictFixture() : base(strictRule: true)
        {
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

        /// <summary>With <see cref="RowDeleteStatus"/>: the Dataverse <c>error.code</c> its body carries (none when null).</summary>
        public string? RowDeleteErrorCode { get; set; }

        /// <summary>
        /// The status Dataverse answers a Restrict refusal with (task 188 slice D): a DELETE of a row that another row names
        /// as <c>sprk_parentdocument</c> fails with <c>0x80040227</c> CannotDeleteDueToAssociation, as
        /// <c>sprk_document_ParentDocument</c> (Delete = Restrict) makes the platform do.
        /// </summary>
        public HttpStatusCode RestrictStatus { get; set; } = HttpStatusCode.BadRequest;

        /// <summary>Set to make the document row GET throw this (an HttpClient timeout is a TaskCanceledException).</summary>
        public Exception? ReadFault { get; set; }

        /// <summary>Set to make the child-document query (task 188 slice D) fail with this status.</summary>
        public HttpStatusCode? ChildReadStatus { get; set; }

        /// <summary>
        /// ebilling-r1 task 094 (ontology-r1 088 gate F1): how many Stored Files name a document as their source
        /// (<c>sprk_sourcedocument</c>, Delete = Restrict). Absent = none.
        /// </summary>
        public ConcurrentDictionary<Guid, int> StoredFileCopies { get; } = new();

        /// <summary>Task 094: set to make the Stored File copy query fail with this status.</summary>
        public HttpStatusCode? StoredFileReadStatus { get; set; }

        /// <summary>Task 094: every Stored File copy query asked (the source document id).</summary>
        public ConcurrentQueue<Guid> StoredFileQueries { get; } = new();

        public void Reset()
        {
            _rows.Clear();
            RowDeleteStatus = null;
            RowDeleteErrorCode = null;
            RestrictStatus = HttpStatusCode.BadRequest;
            ReadFault = null;
            ChildReadStatus = null;
            StoredFileCopies.Clear();
            StoredFileReadStatus = null;
            StoredFileQueries.Clear();
        }

        /// <summary>Removes a row (a child document deleted elsewhere).</summary>
        public void Remove(Guid id) => _rows.TryRemove(id, out _);

        private static HttpResponseMessage DataverseError(HttpStatusCode status, string? code) => code is null
            ? new HttpResponseMessage(status)
            : new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { error = new { code, message = "Dataverse error (test double)" } }),
                    System.Text.Encoding.UTF8, "application/json"),
            };

        /// <summary>Adds a document whose <c>sprk_parentdocument</c> is <paramref name="parentId"/> (an attachment).</summary>
        public Guid AddChild(Guid parentId)
        {
            var id = Guid.NewGuid();
            Add(id, $"drive-{id:N}", $"item-{id:N}");
            _rows[id]["_sprk_parentdocument_value"] = parentId.ToString();
            return id;
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

            // The child-document query: sprk_documents?$filter=_sprk_parentdocument_value eq {id}, evaluated on the rows.
            if (request.Method == HttpMethod.Get && path.EndsWith("/sprk_documents", StringComparison.Ordinal))
            {
                if (ChildReadStatus is { } childStatus)
                    return Task.FromResult(new HttpResponseMessage(childStatus));
                var query = Uri.UnescapeDataString(request.RequestUri.Query);
                var match = System.Text.RegularExpressions.Regex.Match(query, @"_sprk_parentdocument_value eq ([0-9a-fA-F-]{36})");
                if (!match.Success)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
                var parent = match.Groups[1].Value;
                var children = _rows.Values
                    .Where(r => r.TryGetValue("_sprk_parentdocument_value", out var p)
                                && string.Equals(p as string, parent, StringComparison.OrdinalIgnoreCase))
                    .Select(r => new Dictionary<string, object?> { ["sprk_documentid"] = r["sprk_documentid"] })
                    .ToList();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { value = children }),
                        System.Text.Encoding.UTF8, "application/json"),
                });
            }

            // Task 094: the Stored File copy query: sprk_storedfiles?$filter=_sprk_sourcedocument_value eq {id}.
            if (request.Method == HttpMethod.Get && path.EndsWith("/sprk_storedfiles", StringComparison.Ordinal))
            {
                if (StoredFileReadStatus is { } fileStatus)
                    return Task.FromResult(new HttpResponseMessage(fileStatus));
                var query = Uri.UnescapeDataString(request.RequestUri.Query);
                var match = System.Text.RegularExpressions.Regex.Match(query, @"_sprk_sourcedocument_value eq ([0-9a-fA-F-]{36})");
                if (!match.Success)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
                var source = Guid.Parse(match.Groups[1].Value);
                StoredFileQueries.Enqueue(source);
                var copies = Enumerable.Range(0, StoredFileCopies.TryGetValue(source, out var n) ? n : 0)
                    .Select(_ => new Dictionary<string, object?> { ["sprk_storedfileid"] = Guid.NewGuid().ToString() })
                    .ToList();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { value = copies }),
                        System.Text.Encoding.UTF8, "application/json"),
                });
            }

            var open = path.LastIndexOf('(');
            if (!path.Contains("/sprk_documents(", StringComparison.Ordinal) || open < 0
                || !Guid.TryParse(path[(open + 1)..path.LastIndexOf(')')], out var id))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));

            if (request.Method == HttpMethod.Get && ReadFault is { } readFault)
                return Task.FromException<HttpResponseMessage>(readFault);

            if (request.Method == HttpMethod.Delete)
            {
                if (RowDeleteStatus is { } status)
                    return Task.FromResult(DataverseError(status, RowDeleteErrorCode));
                // Delete = Restrict on sprk_document_ParentDocument: refused while any row names this one as its parent.
                if (_rows.Values.Any(r => r.TryGetValue("_sprk_parentdocument_value", out var p)
                                          && string.Equals(p as string, id.ToString(), StringComparison.OrdinalIgnoreCase)))
                    return Task.FromResult(DataverseError(RestrictStatus, DocumentCheckoutService.CannotDeleteDueToAssociationCode));
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

        /// <summary>Task 187: what the live-parent check answers per row (Readable when a test sets nothing).</summary>
        public ConcurrentDictionary<(DeletableKind Kind, Guid Id), LiveParentDecision> LiveParents { get; } = new();

        public override Task<LiveParentDecision> CheckLiveParentsAsync(
            DeletableKind kind, Guid id, CallerPrincipal caller, CancellationToken ct = default) =>
            Task.FromResult(LiveParents.TryGetValue((kind, id), out var decision) ? decision : LiveParentDecision.Readable);

        public ConcurrentDictionary<Guid, Guid> Requesters { get; } = new();

        public Exception? TargetFault { get; set; }

        public Exception? DeleteFault { get; set; }

        public Exception? RequesterFault { get; set; }

        /// <summary>The state a request reads as when a test sets one; a request with none reads as Open (the default case).</summary>
        public ConcurrentDictionary<Guid, ServiceRequestOpenState> States { get; } = new();

        /// <summary>A fault for the state read (the real read throws for a failed Dataverse call).</summary>
        public Exception? StateFault { get; set; }

        /// <summary>Related rows per (entity, service request): what the real probe would find in each relationship.</summary>
        public ConcurrentDictionary<(string Entity, Guid ServiceRequest), bool> RelatedRows { get; } = new();

        /// <summary>The entity whose relationship probe is unreadable (the real probe throws for it).</summary>
        public string? UnreadableRelationship { get; set; }

        /// <summary>Every service request id a row delete was sent for (none when the delete is refused).</summary>
        public ConcurrentQueue<Guid> ServiceRequestDeletes { get; } = new();

        private int _relationshipProbeCalls;

        /// <summary>How many times the relationship probe ran (a refused delete must not reach it).</summary>
        public int RelationshipProbeCalls => Volatile.Read(ref _relationshipProbeCalls);

        public void Reset()
        {
            RelatedRows.Clear();
            Volatile.Write(ref _relationshipProbeCalls, 0);
            RelatedRows.Clear();
            UnreadableRelationship = null;
            ServiceRequestDeletes.Clear();
            Targets.Clear();
            LiveParents.Clear();
            Requesters.Clear();
            States.Clear();
            Volatile.Write(ref _requesterAndStateReads, 0);
            StateFault = null;
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
            if (kind == DeletableKind.ServiceRequest)
            {
                ServiceRequestDeletes.Enqueue(id);
                return Task.FromResult(Requesters.TryRemove(id, out _));
            }
            return Task.FromResult(Targets.TryRemove((kind, id), out _));
        }

        private int _requesterAndStateReads;

        /// <summary>How many combined requester + state reads ran (the delete route reads the row ONCE).</summary>
        public int RequesterAndStateReads => Volatile.Read(ref _requesterAndStateReads);

        public override Task<ServiceRequestRead?> GetServiceRequestRequesterAndStateAsync(Guid serviceRequestId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _requesterAndStateReads);
            if (RequesterFault is { } requesterFault)
                return Task.FromException<ServiceRequestRead?>(requesterFault);
            if (StateFault is { } stateFault)
                return Task.FromException<ServiceRequestRead?>(stateFault);
            if (!Requesters.TryGetValue(serviceRequestId, out var requester))
                return Task.FromResult<ServiceRequestRead?>(null);
            // A row seeded without a scripted state is open.
            var state = States.TryGetValue(serviceRequestId, out var scripted) ? scripted : ServiceRequestOpenState.Open;
            return Task.FromResult<ServiceRequestRead?>(new ServiceRequestRead(requester, state));
        }

        public override Task<bool> HasRelatedRecordsAsync(Guid serviceRequestId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _relationshipProbeCalls);
            // One probe per relationship, as the real method runs them: an unreadable one makes the answer unknown.
            if (UnreadableRelationship is { } entity)
                return Task.FromException<bool>(new DataverseUnavailableException("relationship unreadable: " + entity));
            return Task.FromResult(ServiceRequestRelations.Any(r => RelatedRows.ContainsKey((r.EntityLogicalName, serviceRequestId))));
        }

        public override Task<Guid?> GetServiceRequestRequesterAsync(Guid serviceRequestId, CancellationToken ct = default)
        {
            if (RequesterFault is { } fault)
                return Task.FromException<Guid?>(fault);
            return Task.FromResult(Requesters.TryGetValue(serviceRequestId, out var requester) ? requester : (Guid?)null);
        }
    }
}
