// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/RecordOwnership/**`
//   - Justification: UAC task 194b fix round (F1-a, F3, F4) — a To Do's existing-row ownership question
//     (RecordOwnershipContext.OrderedParentsOf, what the reconciler asks) is the one its WRITER asked when it created it: the
//     same parents in the same order, primary first. A To Do is filed under its RECORD regarding (the lookup carrying
//     sprk_regardingrecordid), then the document or email it was created from — never a carrier before its record. Pinned
//     through each To Do writer's own path (OfficeService over the real route, TodoGenerationService, InquiryReplyTodoCreator);
//     the resolver is doubled at its module boundary and records what each writer asked. F4: a create path that orders the
//     parents by the caller's payload (OwnedChildWrite.ParentsOf, ForChild) asks in the table's primary order instead.
//
// Doubles are module boundaries only (ADR-038 §4).

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

public class TodoOwnershipParityTests
{
    /// <summary><paramref name="row"/> with its attributes in the reverse order (the order Dataverse returns them is not fixed).</summary>
    private static Entity Reversed(Entity row)
    {
        var copy = new Entity(row.LogicalName, row.Id);
        foreach (var (column, value) in row.Attributes.Reverse())
            copy[column] = value;
        return copy;
    }

    /// <summary>The writer's question equals the reconciler's for the created row, in either attribute order, primary first.</summary>
    private static void ShouldAskAsItsWriterAsked(RecordOwnershipContext writer, Entity created, RecordOwnershipParent primary)
    {
        writer.TargetEntityLogicalName.Should().Be(primary.EntityLogicalName, "the writer's primary");
        writer.TargetRecordId.Should().Be(primary.RecordId);
        writer.Parents.Should().NotBeEmpty();
        writer.Parents[0].Should().Be(primary, "the writer lists its primary first");
        RecordOwnershipContext.OrderedParentsOf(created).Should().Equal(writer.Parents, "the full ordered list, primary first");
        RecordOwnershipContext.OrderedParentsOf(Reversed(created)).Should().Equal(writer.Parents,
            "whatever order the attributes come back in");
    }

    // ── OfficeService (POST /api/office/todo): "the record regarding first, then the document or email" ─────────────────

    public static TheoryData<string?, bool, bool> OfficeShapes => new()
    {
        { "Matter", false, true },  // F1-a: a To Do regarding a matter, created from an email — before the fix, the email decided
        { "Matter", true, true },   // and from a Word document as well
        { "Project", true, false },
        { null, true, true },       // carrier-only, both carriers: the document, then the email (nothing is stamped)
        { null, false, true },      // carrier-only: the email
    };

    [Theory(DisplayName = "194b F1-a parity (Office): a To Do's existing-row question is the Office writer's — its record, then its document, then its email — whatever the attribute order")]
    [MemberData(nameof(OfficeShapes))]
    public async Task AnOfficeToDo_IsAskedExactlyAsTheOfficeWriterAsks(string? record, bool document, bool email)
    {
        using var factory = new TodoRegardingTestWebAppFactory();
        using var client = factory.CreateClient();
        var (recordId, documentId, communicationId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/api/office/todo", new CreateTodoRequest
        {
            Name = "Reply to the opposing counsel",
            RegardingEntityType = record,
            RegardingRecordId = record is null ? null : recordId,
            DocumentId = document ? documentId : null,
            CommunicationId = email ? communicationId : null,
            PriorityScore = 50,
            EffortScore = 50,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = factory.CreatedEntities.Should().ContainSingle().Subject;
        var writer = factory.Ownership.Requests.Should().ContainSingle().Subject;
        var primary = record is not null
            ? new RecordOwnershipParent(record == "Matter" ? "sprk_matter" : "sprk_project", recordId)
            : document
                ? new RecordOwnershipParent("sprk_document", documentId)
                : new RecordOwnershipParent("sprk_communication", communicationId);

        ShouldAskAsItsWriterAsked(writer, created, primary);
    }

    // ── TodoGenerationService: the regarding (and its core-ancestor stamps, written by the shared builder) ──────────────

    [Fact(DisplayName = "194b parity (TodoGenerationService): a generated To Do regarding an event, stamped with the event's matter, is asked about the event first, as its writer asked")]
    public async Task AGeneratedToDo_IsAskedExactlyAsTodoGenerationServiceAsks()
    {
        var (matter, @event) = (Guid.NewGuid(), Guid.NewGuid());
        var world = SecureChildShareWorld.Standard()
            .OrdinaryRoot("sprk_matter", matter)
            .OrdinaryChild("sprk_event", @event, ("sprk_regardingmatter", "sprk_matter", matter));
        var created = new List<Entity>();
        var dataverse = new Mock<IDataverseService>(MockBehavior.Loose);
        dataverse.Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
        dataverse.Setup(d => d.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => new Entity(table, id));
        var events = new Mock<IEventDataverseService>(MockBehavior.Loose);
        var catalog = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);
        catalog.Setup(c => c.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Entity?)null);
        var ownership = new RecordOwnershipResolverDouble();
        var services = new ServiceCollection()
            .AddSingleton(dataverse.Object).AddSingleton(events.Object).AddSingleton(catalog.Object)
            .AddSingleton<IRecordOwnershipResolver>(ownership)
            .BuildServiceProvider();
        var service = new TodoGenerationService(
            services, NullLogger<TodoGenerationService>.Instance, Options.Create(new TodoGenerationOptions()));
        service.SetDataverseForTest(dataverse.Object, events.Object);
        service.SetRegardingBuilderForTest(new TodoRegardingBuilder(
            catalog.Object, SecureChildShareWorld.CoreAncestorsOver(() => world), NullLogger<TodoRegardingBuilder>.Instance));
        service.SetOwnershipResolverForTest(ownership);

        await service.CreateTodoAsync("Budget Alert: X", "sprk_event", @event, "Hearing", ct: CancellationToken.None);

        var row = created.Should().ContainSingle().Subject;
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id.Should().Be(matter, "the shared builder stamps the event's matter");
        ShouldAskAsItsWriterAsked(ownership.Requests.Should().ContainSingle().Subject, row, new RecordOwnershipParent("sprk_event", @event));
    }

    // ── InquiryReplyTodoCreator: the matter regarding, through the shared builder ──────────────────────────────────────

    [Fact(DisplayName = "194b parity (InquiryReplyTodoCreator): the inquiry-outcome To Do is asked about its matter first, as its writer asked")]
    public async Task AnInquiryOutcomeToDo_IsAskedExactlyAsInquiryReplyTodoCreatorAsks()
    {
        var (matter, serviceRequest, communication) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var world = SecureChildShareWorld.Standard().OrdinaryRoot("sprk_matter", matter);
        var created = new List<Entity>();
        var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entities.Setup(e => e.RetrieveAsync("sprk_communication", communication, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", communication)
            {
                ["sprk_direction"] = new OptionSetValue(InquiryDispositionService.CommunicationIncoming),
                ["sprk_regardingservicerequest"] = new EntityReference(InquiryDispositionService.ServiceRequest, serviceRequest),
            });
        entities.Setup(e => e.RetrieveAsync(InquiryDispositionService.ServiceRequest, serviceRequest, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(InquiryDispositionService.ServiceRequest, serviceRequest)
            {
                ["sprk_direction"] = new OptionSetValue(BudgetInquiryExecutor.DirectionOutbound),
                ["statecode"] = new OptionSetValue(0),
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter),
            });
        entities.Setup(e => e.RetrieveAsync("sprk_matter", matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", matter));
        entities.Setup(e => e.RetrieveAsync("sprk_todo", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no such To Do"));
        entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
        var catalog = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);
        catalog.Setup(c => c.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Entity?)null);
        var ownership = new RecordOwnershipResolverDouble();
        var creator = new InquiryReplyTodoCreator(
            entities.Object, SecureChildShareWorld.CoreAncestorsOver(() => world), ownership, catalog.Object, TimeProvider.System,
            NullLogger<InquiryReplyTodoCreator>.Instance);

        (await creator.OnReplyArrivedAsync(communication, CancellationToken.None)).Should().Be(InquiryReplyTodoOutcome.Created);

        ShouldAskAsItsWriterAsked(
            ownership.Requests.Should().ContainSingle().Subject, created.Should().ContainSingle().Subject,
            new RecordOwnershipParent("sprk_matter", matter));
    }

    // ── F4: a create path never takes its primary from the caller's payload order ─────────────────────────────────────

    [Theory(DisplayName = "194b F4 must fire: a budget whose payload names its project before its matter is asked about the matter first (the table's primary), as the reconciler asks it")]
    [InlineData(true)]  // the project first in the payload: before the fix, the project was the primary
    [InlineData(false)]
    public void ACreatedBudget_TakesItsTablesPrimary_NotThePayloadOrder(bool projectFirst)
    {
        var (matter, project) = (Guid.NewGuid(), Guid.NewGuid());
        var lookups = new[]
        {
            new DataverseWriteItemMapper.MappedLookup("sprk_project", "sprk_project", "sprk_projects", project),
            new DataverseWriteItemMapper.MappedLookup("sprk_matter", "sprk_matter", "sprk_matters", matter),
        };
        var item = new DataverseWriteItemMapper.MappedItem("{}", ["sprk_project", "sprk_matter"])
        {
            Lookups = projectFirst ? lookups : lookups.Reverse().ToArray(),
        };
        var row = new Entity("sprk_budget", Guid.NewGuid());
        foreach (var lookup in item.Lookups)
            row[lookup.Column] = new EntityReference(lookup.RelatedTable, lookup.RecordId);

        var writer = RecordOwnershipContext.ForParents(OwnedChildWrite.ParentsOf("sprk_budget", item)); // BudgetInquiryExecutor / create_record

        writer.TargetEntityLogicalName.Should().Be("sprk_matter", "a budget's primary is its matter (SecureChildLineage order)");
        writer.Parents.Should().Equal(RecordOwnershipContext.OrderedParentsOf(row), "the question the reconciler asks the created row");
        RecordOwnershipContext.ForChild(row).TargetRecordId.Should().Be(matter, "ForChild with no explicit primary asks alike");
        RecordOwnershipContext.ForChild(row).Parents.Should().Equal(writer.Parents);
    }

    [Fact(DisplayName = "194b F4: a create_record To Do whose body names the pair takes its record regarding first, before an email carrier the payload lists first")]
    public void ACreateRecordToDo_TakesItsRecordRegardingFirst_NotThePayloadOrder()
    {
        var (matter, communication) = (Guid.NewGuid(), Guid.NewGuid());
        var item = new DataverseWriteItemMapper.MappedItem(
            $$"""{"sprk_name":"x","sprk_regardingrecordid":"{{matter:D}}"}""", ["sprk_regardingcommunication", "sprk_regardingmatter"])
        {
            Lookups =
            [
                new("sprk_regardingcommunication", "sprk_communication", "sprk_communications", communication),
                new("sprk_regardingmatter", "sprk_matter", "sprk_matters", matter),
            ],
        };

        OwnedChildWrite.ParentsOf("sprk_todo", item).Should().Equal(
            new RecordOwnershipParent("sprk_matter", matter), new RecordOwnershipParent("sprk_communication", communication));
    }

    // ── K2: a record thread's filing is read in one fixed order ──────────────────────────────────────────────────────

    [Fact(DisplayName = "194b K2 must fire: a record thread's parents come back in one order whatever order its attributes were read in")]
    public void ARecordThreadsParents_DoNotDependOnTheAttributeOrder()
    {
        var thread = new Entity("sprk_communicationthread", Guid.NewGuid())
        {
            ["sprk_regardingproject"] = new EntityReference("sprk_project", Guid.NewGuid()),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid()),
        };

        RecordThreadFiling.ParentsOf(Reversed(thread)).Should().Equal(RecordThreadFiling.ParentsOf(thread));
    }
}
