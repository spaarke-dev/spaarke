using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.Shared.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// Protects write-path invariant I-6 for the Office writers (spaarkeai-word-add-in-r1 task 080): <b>every record the
/// Office surface creates is owned by a business unit's DEFAULT OWNER TEAM — resolved record-first — or it is not
/// created at all.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure mode.</b> These creates run app-only. With no explicit owner, Dataverse makes the BFF application
/// user the owner, and that user sits in the ROOT business unit — measured 2026-09-22, all 512 <c>sprk_document</c>
/// rows. Dataverse Deep depth reaches a unit's DESCENDANTS, never its parent, so no child-unit user could read a
/// document they had just saved, and task 063's Run Index and task 064's To Do refused every ordinary user.
/// </para>
/// <para>
/// <b>Why the refusals matter as much as the owner.</b> A writer that fell back to app ownership when no team resolved
/// would pass every happy-path test here and silently reintroduce the defect. So each writer is also asserted to
/// REFUSE — in its own error contract — and to have written nothing.
/// </para>
/// <para>
/// Companion: <c>RecordOwnershipResolverTests</c> pins the resolution order and both refuse branches. These tests pin
/// that the WRITERS ask (with the right target) and write what they are told. The resolver is doubled at its module
/// boundary (<see cref="RecordOwnershipResolverDouble"/>); everything else is the real route, filters and services.
/// </para>
/// </remarks>
[Trait("status", "new")]
public class OfficeRecordOwnershipTests
{
    private const string SaveContainer = "b!test-office-save-drive";

    private static readonly byte[] Docx = MinimalDocx.Create("owned draft");
    private static readonly EntityReference OwnerTeam = new("team", RecordOwnershipResolverDouble.DefaultTeamId);

    /// <summary>Task 146 c1-r1: the signed-in Office user's object id, for the tests that see them recorded as creator.</summary>
    private static readonly Guid SavingUserOid = Guid.Parse("c1c1c1c1-0000-4000-8000-00000000000d");

    // =====================================================================================
    // sprk_document — POST /api/office/save
    // =====================================================================================

    [Fact]
    public async Task Save_FiledToARecord_OwnsTheNewDocumentByThatRecordsTeam_AndHandsTheSameTeamToTheWorker()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var matterId = Guid.NewGuid();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Oid", SavingUserOid.ToString()); // a real (GUID) object id, as in production

        var response = await client.PostAsJsonAsync("/api/office/save", new SaveRequest
        {
            ContentType = SaveContentType.Document,
            TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = matterId },
            Document = new DocumentMetadata { FileName = "Owned.docx", ContentBase64 = Convert.ToBase64String(Docx) },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.CreatedDocumentOwningTeams.Should().Equal(RecordOwnershipResolverDouble.DefaultTeamId);

        var asked = factory.Ownership.Requests.Should().ContainSingle().Subject;
        // The friendly spelling the add-in sends; the resolver maps it to sprk_matter (RecordOwnershipResolverTests).
        asked.TargetEntityLogicalName.Should().Be("matter", "a filed document follows its record, not its uploader");
        asked.TargetRecordId.Should().Be(matterId);

        // The worker creates an email's attachment children; it must give them the parent's team, not resolve anew.
        var payload = world.FinalizationPayloads.Should().ContainSingle().Subject;
        Guid.Parse(OfficeVersionSaveWorld.PayloadValue(payload, "OwningTeamId")!)
            .Should().Be(RecordOwnershipResolverDouble.DefaultTeamId);

        // c1-r1 (owner round 13 item 9): the saving user (asked by object id) is recorded on the app-created document —
        // and carried to the worker with the team, so the children it creates record them too.
        asked.RequestedBy!.ObjectId.Should().Be(SavingUserOid);
        world.CreatedDocumentPersons.Should().Equal(RecordOwnershipResolverDouble.DefaultRequesterPersonId);
        Guid.Parse(OfficeVersionSaveWorld.PayloadValue(payload, "CreatedByPersonId")!)
            .Should().Be(RecordOwnershipResolverDouble.DefaultRequesterPersonId);
    }

    [Fact]
    public async Task Save_FiledToNothing_AsksForTheCallersBusinessUnitTeam_WithNoTarget()
    {
        // The Word ribbon quick-save and every pane save with no "Related to" — the save spine's mainline (task 065).
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", new SaveRequest
        {
            ContentType = SaveContentType.Document,
            Document = new DocumentMetadata { FileName = "Unfiled.docx", ContentBase64 = Convert.ToBase64String(Docx) },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "a save with no related record is a required use case");
        world.CreatedDocumentOwningTeams.Should().Equal(RecordOwnershipResolverDouble.DefaultTeamId);
        factory.Ownership.Requests.Should().ContainSingle().Which.HasTarget.Should().BeFalse();
    }

    [Fact]
    public async Task Save_WhenNoOwnerTeamResolves_Returns403Office022_AndWritesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        factory.Ownership.TeamId = null;

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", new SaveRequest
        {
            ContentType = SaveContentType.Document,
            TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
            Document = new DocumentMetadata { FileName = "Refused.docx", ContentBase64 = Convert.ToBase64String(Docx) },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeOf(response)).Should().Be("OFFICE_022");
        // Refused before the job row, the upload and the document row — not after the bytes moved.
        world.ShouldHaveWrittenNothing();
        world.SpeItems.Values.Should().NotContain(i => i.DriveId == SaveContainer && i.Name == "Refused.docx");
    }

    [Fact]
    public async Task VersionSave_NeverAsksForAnOwner_AndStillSucceedsWhenNoTeamWouldResolve()
    {
        // A version save writes a new SPE version of an EXISTING document and creates no row, so its row keeps the
        // owner it has. Proven by making every ownership answer a refusal: the version save must not notice.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var (documentId, _) = world.SeedDocument(SaveContainer, "Brief.docx", MinimalDocx.Create("v1"));
        factory.Ownership.TeamId = null;

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, MinimalDocx.Create("v2")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Ownership.Requests.Should().BeEmpty();
        world.DocumentCreates.Should().Be(0);
    }

    // =====================================================================================
    // sprk_todo — POST /api/office/todo (record-first: record regarding → document → communication → caller)
    // =====================================================================================

    [Fact]
    public async Task CreateTodo_WithARecordAndADocument_IsOwnedByTheRecordsTeam_NotTheDocuments()
    {
        using var factory = new TodoRegardingTestWebAppFactory();
        var matterId = Guid.NewGuid();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Oid", SavingUserOid.ToString()); // a real (GUID) object id, as in production

        var response = await client.PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Review red-lines",
            RegardingEntityType = "Matter",
            RegardingRecordId = matterId,
            DocumentId = Guid.NewGuid(),
            PriorityScore = 50,
            EffortScore = 50,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var todo = factory.CreatedEntities.Should().ContainSingle().Subject;
        todo.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(OwnerTeam);

        var asked = factory.Ownership.Requests.Should().ContainSingle().Subject;
        asked.TargetEntityLogicalName.Should().Be("sprk_matter");
        asked.TargetRecordId.Should().Be(matterId);

        // c1-r1 (owner round 13 item 9): the Office user who asked is recorded on the app-created To Do.
        asked.RequestedBy!.ObjectId.Should().Be(SavingUserOid);
        todo.GetAttributeValue<EntityReference>("sprk_createdbyperson").Id
            .Should().Be(RecordOwnershipResolverDouble.DefaultRequesterPersonId);
    }

    [Fact]
    public async Task CreateTodo_WithOnlyADocument_IsOwnedByTheDocumentsTeam()
    {
        using var factory = new TodoRegardingTestWebAppFactory();
        var documentId = Guid.NewGuid();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Follow up on the draft",
            DocumentId = documentId,
            PriorityScore = 50,
            EffortScore = 50,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.CreatedEntities.Should().ContainSingle()
            .Which.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(OwnerTeam);

        var asked = factory.Ownership.Requests.Should().ContainSingle().Subject;
        asked.TargetEntityLogicalName.Should().Be("sprk_document");
        asked.TargetRecordId.Should().Be(documentId);
    }

    [Fact]
    public async Task CreateTodo_WhenNoOwnerTeamResolves_Returns403Office022_AndCreatesNothing()
    {
        using var factory = new TodoRegardingTestWebAppFactory();
        factory.Ownership.TeamId = null;

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Nobody owns this",
            RegardingEntityType = "Project",
            RegardingRecordId = Guid.NewGuid(),
            PriorityScore = 50,
            EffortScore = 50,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeOf(response)).Should().Be("OFFICE_022", "distinct from OFFICE_010's generic create failure");
        factory.CreatedEntities.Should().BeEmpty();
    }

    // =====================================================================================
    // sprk_todo Assigned To defaults (task 083, #1044 writer half): task 080 made the owner a BU default
    // team, so nothing on the row named the person it was for, and the Daily Briefing (UAC-r2 task 152's
    // people-targeting surface, which matches sprk_assignedto through the SAME task-141 link) could not
    // find it. These pin that an unassigned To Do defaults to the caller's linked contact, that an explicit
    // assignee is never overridden, and that a caller with no linked contact still gets their To Do.
    // =====================================================================================

    [Fact]
    public async Task CreateTodo_NoAssignee_DefaultsSprkAssignedToToTheCallersLinkedContact_OwnerTeamUnchanged()
    {
        using var factory = new TodoRegardingTestWebAppFactory();
        var callerSystemUserId = Guid.NewGuid();
        var callerContactId = Guid.NewGuid();
        var matterId = Guid.NewGuid();

        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(callerSystemUserId.ToString("D")));
        factory.Identity
            .Setup(i => i.ResolveAsync(callerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(callerSystemUserId, ContactId: callerContactId));

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Review red-lines",
            RegardingEntityType = "Matter",
            RegardingRecordId = matterId,
            PriorityScore = 50,
            EffortScore = 50,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = factory.CreatedEntities.Should().ContainSingle().Subject;
        created.GetAttributeValue<EntityReference>("sprk_assignedto")
            .Should().BeEquivalentTo(new EntityReference("contact", callerContactId),
                "no assignee was chosen, so the caller's own linked contact (task 141) names the To Do");
        // 080's team-ownership invariant is untouched by this default.
        created.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(OwnerTeam);
    }

    [Fact]
    public async Task CreateTodo_ExplicitAssignee_IsNeverOverriddenByTheCallersContact()
    {
        using var factory = new TodoRegardingTestWebAppFactory();
        var callerSystemUserId = Guid.NewGuid();
        var callerContactId = Guid.NewGuid();
        var explicitAssigneeContactId = Guid.NewGuid();

        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(callerSystemUserId.ToString("D")));
        factory.Identity
            .Setup(i => i.ResolveAsync(callerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(callerSystemUserId, ContactId: callerContactId));

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Follow up on the draft",
            DocumentId = Guid.NewGuid(),
            AssignedToContactId = explicitAssigneeContactId,
            PriorityScore = 50,
            EffortScore = 50,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.CreatedEntities.Should().ContainSingle()
            .Which.GetAttributeValue<EntityReference>("sprk_assignedto")
            .Should().BeEquivalentTo(
                new EntityReference("contact", explicitAssigneeContactId),
                "an explicitly chosen assignee always wins over the caller's own contact");
    }

    [Fact]
    public async Task CreateTodo_NoAssigneeAndNoLinkedContact_StillCreates_WithNoAssignedTo_AndLogsAWarning()
    {
        using var factory = new TodoRegardingTestWebAppFactory();
        var callerSystemUserId = Guid.NewGuid();

        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(callerSystemUserId.ToString("D")));
        // No ContactId set — PersonIdentity's default is null, i.e. "no linked contact" (never a throw).
        factory.Identity
            .Setup(i => i.ResolveAsync(callerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(callerSystemUserId));

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Nobody to name",
            DocumentId = Guid.NewGuid(),
            PriorityScore = 50,
            EffortScore = 50,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, "a missing contact link must never refuse the create");
        var created = factory.CreatedEntities.Should().ContainSingle().Subject;
        created.Contains("sprk_assignedto").Should().BeFalse(
            "no assignee was chosen and the caller has no linked contact, so the column stays absent");

        factory.Logs.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("todo_assignee_unset")
            && e.Message.Contains(callerSystemUserId.ToString("D")),
            "the missing link must be logged, naming the caller");
    }

    // =====================================================================================
    // sprk_invoice — POST /api/office/quickcreate/invoice (filed against nothing: the caller's unit)
    // =====================================================================================

    [Fact]
    public async Task QuickCreateInvoice_IsOwnedByTheCallersTeam_NotTheCaller()
    {
        var callerSystemUserId = Guid.NewGuid();
        using var factory = InvoiceFactory(callerSystemUserId, out var created);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/quickcreate/invoice", new QuickCreateRequest { Name = "INV-0080" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoice = created.Should().ContainSingle().Subject;
        invoice.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(OwnerTeam);
        factory.Ownership.Requests.Should().ContainSingle()
            .Which.CallerSystemUserId.Should().Be(callerSystemUserId);
        // c1-r1 (owner round 13 item 9): the Office user is recorded on the app-created invoice.
        invoice.GetAttributeValue<EntityReference>("sprk_createdbyperson").Id.Should().Be(callerSystemUserId);
    }

    [Fact]
    public async Task QuickCreateInvoice_WhenNoOwnerTeamResolves_Returns403Office022_AndCreatesNothing()
    {
        // Was best-effort until task 080: an unresolved owner left the invoice app-owned in ROOT and still created it.
        using var factory = InvoiceFactory(Guid.NewGuid(), out var created);
        factory.Ownership.TeamId = null;

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/quickcreate/invoice", new QuickCreateRequest { Name = "INV-0081" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeOf(response)).Should().Be("OFFICE_022");
        created.Should().BeEmpty();
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

    private static OfficeQuickCreateTestWebAppFactory InvoiceFactory(Guid callerSystemUserId, out List<Entity> created)
    {
        var factory = new OfficeQuickCreateTestWebAppFactory();
        var captured = new List<Entity>();
        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(callerSystemUserId.ToString("D")));
        factory.Entities
            .Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) => captured.Add(entity))
            .ReturnsAsync(Guid.NewGuid());
        created = captured;
        return factory;
    }

    private static async Task<string?> ErrorCodeOf(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }
}
