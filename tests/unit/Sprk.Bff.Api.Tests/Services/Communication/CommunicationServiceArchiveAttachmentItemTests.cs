using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// #2246: the on-demand archive (<see cref="CommunicationService.ArchiveExistingAsync"/>) creates a <c>sprk_document</c> for
/// an attachment row that has an item but no document. The row's <c>sprk_graphitemid</c> / <c>sprk_graphdriveid</c> are
/// writable by a user or an app-only generic writer, so the archive must verify the item against server-trusted facts
/// (Graph's own record of the item, the communication's derived container) before binding a document's pointer and its
/// field-secured bound copy to it. Otherwise a foreign item is laundered through the binding check.
/// </summary>
/// <remarks>
/// Module-boundary doubles only (ADR-038 §4): the Dataverse writer (<see cref="IGenericEntityService"/>), the job queue and
/// the Graph item read behind the REAL <see cref="RecordContainerResolver"/> (task 166's pointer world). The communication
/// is already archived, so only the attachment step runs.
/// </remarks>
public class CommunicationServiceArchiveAttachmentItemTests
{
    private const string ArchiveContainer = "drive-archive";
    private const string AttachmentEntity = "sprk_communicationattachment";
    private const string DocumentEntity = "sprk_document";

    private static readonly string BffApp = TestRecordContainerResolver.PointerWorldBffApplicationId.ToString("D");

    private sealed class Harness
    {
        public required CommunicationService Sut { get; init; }
        public required Guid CommunicationId { get; init; }
        public required List<Entity> Created { get; init; }
        public required List<(string Entity, Guid Id, Dictionary<string, object> Fields)> Updates { get; init; }
        public required List<JobContract> Jobs { get; init; }
    }

    /// <summary>An item the archive wrote for <paramref name="communicationId"/>: its name prefix, uploaded by the BFF.</summary>
    private static SpeItemCreator ArchiveItem(Guid communicationId, string name)
        => new($"{communicationId:N}_{name}", UserObjectId: null, ApplicationId: BffApp);

    private static Entity AttachmentRow(string name, string? drive, string item)
    {
        var row = new Entity(AttachmentEntity, Guid.NewGuid()) { ["sprk_name"] = name, ["sprk_graphitemid"] = item };
        if (drive is not null)
        {
            row["sprk_graphdriveid"] = drive;
        }

        return row;
    }

    [Fact]
    public async Task AGenuineAttachmentItem_IsArchivedAsBefore()
    {
        // must-not-fire: the item is in the communication's container, uploaded by the BFF, named for THIS communication.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            AttachmentRow("contract.pdf", ArchiveContainer, "item-own"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(1);
        result.AttachmentsRefusedUnverified.Should().Be(0);
        var doc = h.Created.Should().ContainSingle().Subject;
        doc.GetAttributeValue<string>("sprk_graphitemid").Should().Be("item-own");
        doc.GetAttributeValue<string>(DocumentPointerBinding.BoundItemIdColumn).Should().Be("item-own");
        doc.GetAttributeValue<string>("sprk_graphdriveid").Should().Be(ArchiveContainer);
        doc.GetAttributeValue<EntityReference>("ownerid").Should().NotBeNull("task 146 owner resolution still applies");
        h.Updates.Should().ContainSingle(u => u.Entity == AttachmentEntity, "the document is linked back for idempotency");
        h.Jobs.Should().ContainSingle("the Document Profile job is still enqueued");
    }

    [Fact]
    public async Task AnAttachmentRowWithNoDrive_FallsBackToTheArchiveContainer_AndIsStillVerified()
    {
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "scan.pdf"),
            AttachmentRow("scan.pdf", drive: null, "item-own"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(1);
        h.Created.Single().GetAttributeValue<string>("sprk_graphdriveid").Should().Be(ArchiveContainer);
    }

    [Fact]
    public async Task AnotherCommunicationsAttachmentItem_InTheSharedArchiveContainer_IsNotArchived_AndIsCounted()
    {
        // must-fire: the #2246 scenario. The row was re-pointed at communication D's attachment in the shared container.
        var other = Guid.NewGuid();
        var h = Build((world, c) =>
            {
                world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "mine.pdf");
                world.Items[(ArchiveContainer, "item-foreign")] = ArchiveItem(other, "privileged.pdf");
            },
            AttachmentRow("mine.pdf", ArchiveContainer, "item-own"),
            AttachmentRow("privileged.pdf", ArchiveContainer, "item-foreign"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(1);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().NotContain(d => d.GetAttributeValue<string>("sprk_graphitemid") == "item-foreign");
        h.Created.Should().NotContain(d => d.GetAttributeValue<string>(DocumentPointerBinding.BoundItemIdColumn) == "item-foreign",
            "a foreign item is never bound to a new document");
        h.Updates.Should().ContainSingle("only the verified attachment is linked back");
        h.Jobs.Should().ContainSingle();
    }

    [Fact]
    public async Task AnItemInADriveThatIsNotTheCommunicationsContainer_IsNotArchived_AndIsCounted()
    {
        // must-fire: right name, uploaded by the BFF, but in another container than the one derived for the communication.
        var h = Build((world, c) => world.Items[("drive-elsewhere", "item-x")] = ArchiveItem(c, "contract.pdf"),
            AttachmentRow("contract.pdf", "drive-elsewhere", "item-x"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task AnItemThatDoesNotExistInTheDrive_IsNotArchived_AndIsCounted()
    {
        // must-fire: Graph reports no such item in the drive the row names.
        var h = Build((world, _) => world.Items[(ArchiveContainer, "item-gone")] = null,
            AttachmentRow("gone.pdf", ArchiveContainer, "item-gone"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task AnItemAPersonUploaded_WithThisCommunicationsPrefix_IsNotArchived()
    {
        // must-fire: a person with access to the container named a file like the archive would; the archive did not write it.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-planted")] =
                new SpeItemCreator($"{c:N}_planted.pdf", TestRecordContainerResolver.PointerWorldCreatorObjectId.ToString("D"), BffApp),
            AttachmentRow("planted.pdf", ArchiveContainer, "item-planted"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheVerificationReadFails_NothingIsCreated()
    {
        // fail-closed: Graph is unavailable.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            itemReadFault: new HttpRequestException("Graph unavailable"),
            rows: AttachmentRow("contract.pdf", ArchiveContainer, "item-own"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
        h.Updates.Should().BeEmpty();
        h.Jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenNoVerifierIsAvailable_NothingIsCreated()
    {
        // fail-closed: a host that wires no scope factory cannot verify, so it archives nothing.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            noScopeFactory: true,
            rows: AttachmentRow("contract.pdf", ArchiveContainer, "item-own"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static Harness Build(
        Action<TestRecordContainerResolver.DocumentPointerWorld, Guid> arrange, params Entity[] rows)
        => Build(arrange, itemReadFault: null, noScopeFactory: false, rows);

    private static Harness Build(
        Action<TestRecordContainerResolver.DocumentPointerWorld, Guid> arrange,
        Exception? itemReadFault = null,
        bool noScopeFactory = false,
        params Entity[] rows)
    {
        var communicationId = Guid.NewGuid();

        // The REAL resolver: a communication regarding nothing derives to Communication:ArchiveContainerId.
        var world = new TestRecordContainerResolver.DocumentPointerWorld
        {
            ArchiveContainerId = ArchiveContainer,
            ItemReadFault = itemReadFault,
        };
        world.Rows[("sprk_communication", communicationId)] = new Entity("sprk_communication", communicationId);
        arrange(world, communicationId);
        var resolver = world.Build();

        var created = new List<Entity>();
        var updates = new List<(string, Guid, Dictionary<string, object>)>();
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", communicationId));
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == AttachmentEntity), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(rows.ToList()));
        // Already archived: the .eml step is skipped, so only the attachment step runs.
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == DocumentEntity), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new(DocumentEntity, Guid.NewGuid()) }));
        entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity e, CancellationToken _) =>
            {
                created.Add(e);
                return Guid.NewGuid();
            });
        entityService
            .Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback((string entity, Guid id, Dictionary<string, object> fields, CancellationToken _) => updates.Add((entity, id, fields)))
            .Returns(Task.CompletedTask);

        var options = new CommunicationOptions
        {
            ApprovedSenders = new[]
            {
                new ApprovedSenderConfig { Email = "noreply@contoso.com", DisplayName = "Contoso", IsDefault = true }
            },
            DefaultMailbox = "noreply@contoso.com",
            ArchiveContainerId = ArchiveContainer,
        };
        var accountService = new CommunicationAccountService(
            Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options), accountService, Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());
        var dispatcher = new CommunicationChannelDispatcher(
            Array.Empty<ICommunicationChannelSender>(), Array.Empty<ICommunicationArchiver>());

        var jobs = new List<JobContract>();
        var sut = new CommunicationService(
            dispatcher,
            senderValidator,
            Mock.Of<ICommunicationDataverseService>(),
            entityService.Object,
            Mock.Of<IDocumentDataverseService>(),
            accountService,
            BuildJobQueue(jobs),
            Mock.Of<ICommunicationEnrichmentService>(),
            Microsoft.Extensions.Options.Options.Create(options),
            CoreAncestorResolverFixtures.Inert(),
            new RecordOwnershipResolverDouble(),
            Mock.Of<ILogger<CommunicationService>>(),
            scopeFactory: noScopeFactory ? null : ScopeServing(resolver));

        return new Harness
        {
            Sut = sut, CommunicationId = communicationId, Created = created, Updates = updates, Jobs = jobs,
        };
    }

    private static IServiceScopeFactory ScopeServing(RecordContainerResolver resolver)
    {
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(RecordContainerResolver))).Returns(resolver);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private static JobSubmissionService BuildJobQueue(List<JobContract> jobs)
    {
        var sbOptions = new Mock<IOptions<ServiceBusOptions>>();
        sbOptions.Setup(o => o.Value).Returns(new ServiceBusOptions
        {
            QueueName = "test-jobs",
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        });
        var queue = new Mock<JobSubmissionService>(
            MockBehavior.Loose, sbOptions.Object, Mock.Of<ILogger<JobSubmissionService>>(), new Mock<ServiceBusClient>().Object);
        queue
            .Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => jobs.Add(job))
            .Returns(Task.CompletedTask);
        return queue.Object;
    }
}
