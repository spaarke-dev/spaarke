using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Services.Email;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Auth.SpeAdmin;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Sprk.Bff.Api.Workers.Office;
using Sprk.Bff.Api.Workers.Office.Messages;
using Xunit;
using SaveContentType = Sprk.Bff.Api.Models.Office.SaveContentType;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 030b (FR-18, word gaps G1/G2): every <c>sprk_document</c> an Office save creates
/// is filed IN its create — the association is a column of the one create call, never of the follow-up update — and is
/// owned by the team the owner class decided over that same association.
/// </summary>
/// <remarks>
/// <para>The document save stays outside <c>RecordCreationService</c> (W3 direction, 2026-11-06, a named exception). The
/// writes under test are the save's create (<c>OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync</c>)
/// and the worker's two (<see cref="UploadFinalizationWorker.ProcessAsync"/>: the fallback create and each attachment child),
/// doubled only at the Dataverse document seam, SPE and the queues (KEEP path: tests/integration/data-mutation/**).</para>
/// <para>Before 030b the association went in the update, so a document existed, however briefly, filed nowhere, and stayed
/// so when the update failed.</para>
/// </remarks>
[Trait("status", "task-030b-field-mapping")]
public class OfficeDocumentCreateAssociationTests
{
    private static readonly Guid RecordId = Guid.Parse("030b030b-0000-4000-8000-0000000000a1");
    private static readonly Guid SavedTeamId = Guid.Parse("030b030b-0000-4000-8000-0000000000e1");

    // ── The save's create (OfficeDocumentPersistence) ──────────────────────────────────────────────────────────────

    /// <summary>AC-1: every accepted spelling, and the column it is created with.</summary>
    [Theory]
    [InlineData("matter", "sprk_matter", "sprk_matter")]
    [InlineData("sprk_matter", "sprk_matter", "sprk_matter")]
    [InlineData("project", "sprk_project", "sprk_project")]
    [InlineData("sprk_project", "sprk_project", "sprk_project")]
    [InlineData("invoice", "sprk_invoice", "sprk_invoice")]
    [InlineData("sprk_invoice", "sprk_invoice", "sprk_invoice")]
    [InlineData("workassignment", "sprk_workassignment", "sprk_workassignment")]
    [InlineData("sprk_workassignment", "sprk_workassignment", "sprk_workassignment")]
    [InlineData("event", "sprk_relatedevent", "sprk_event")]
    [InlineData("sprk_event", "sprk_relatedevent", "sprk_event")]
    [InlineData("todo", "sprk_relatedtodo", "sprk_todo")]
    [InlineData("sprk_todo", "sprk_relatedtodo", "sprk_todo")]
    [InlineData("contact", "sprk_relatedcontact", "contact")]
    public async Task Save_FiledToARecord_IsCreatedWithTheAssociation_AndTheUpdateCarriesNone(
        string entityType, string column, string targetTable)
    {
        var world = new PersistenceWorld();

        await world.SaveAsync(DocumentSave(entityType));

        var created = world.Creates.Should().ContainSingle("one create call").Subject;
        created.Association.Should().NotBeNull("the document is filed in its create");
        created.Association!.Field.LogicalName.Should().Be(column);
        created.Association.Field.TargetEntityLogicalName.Should().Be(targetTable);
        created.Association.RecordId.Should().Be(RecordId);
        created.OwningTeamId.Should().Be(SavedTeamId, "AC-5: the team the save's owner question returned");

        var update = world.Updates.Should().ContainSingle("one pointer update, as before").Subject.Update;
        CarriesNoAssociation(update).Should().BeTrue("AC-1: no association update is issued");
        CarriesNoIndexColumn(update).Should().BeTrue("AC-6 (FR-40): the index columns are the pipeline's");
        update.GraphItemId.Should().Be("item1", "the pointer stays in the update (W3 ruling covers the association only)");
    }

    /// <summary>AC-7: a type with no sprk_document column, or none at all, files the document nowhere and the save goes on.</summary>
    [Theory]
    [InlineData("account")]
    [InlineData("sprk_unknown")]
    public async Task Save_ToATypeWithNoDocumentColumn_IsCreatedUnassociated_AndWarns(string entityType)
    {
        var world = new PersistenceWorld();

        await world.SaveAsync(DocumentSave(entityType));

        world.Creates.Should().ContainSingle().Which.Association.Should().BeNull();
        CarriesNoAssociation(world.Updates.Should().ContainSingle().Subject.Update).Should().BeTrue();
        world.Logs.Lines.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("UNASSOCIATED"));
    }

    [Fact]
    public async Task Save_WithNoTarget_IsCreatedUnassociated()
    {
        var world = new PersistenceWorld();

        await world.SaveAsync(DocumentSave(entityType: null));

        world.Creates.Should().ContainSingle().Which.Association.Should().BeNull();
        CarriesNoAssociation(world.Updates.Should().ContainSingle().Subject.Update).Should().BeTrue();
    }

    /// <summary>
    /// AC-8: the pre-assigned id names a row that already exists. Dataverse refuses the create (duplicate key); the save
    /// throws to its existing failure path and never writes the existing row.
    /// </summary>
    [Fact]
    public async Task Save_WithAGivenIdThatAlreadyExists_IsRefused_AndTheExistingRowIsNeverUpdated()
    {
        var existingId = Guid.Parse("030b030b-0000-4000-8000-00000000dead");
        var world = new PersistenceWorld { ExistingIds = { existingId } };

        var act = () => world.SaveAsync(DocumentSave("matter"), preAssignedDocumentId: existingId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*duplicate key*");
        world.CreateAttempts.Should().ContainSingle().Which.Id.Should().Be(existingId, "the given id is offered to a create, never an upsert");
        world.Updates.Should().BeEmpty("a refused create writes nothing to the row that already holds the id");
    }

    // ── The worker's creates (UploadFinalizationWorker) ────────────────────────────────────────────────────────────

    /// <summary>AC-2 + AC-5: the fallback create carries the association and the CARRIED owner (the resolver is not asked).</summary>
    [Fact]
    public async Task Worker_FallbackCreate_IsCreatedWithTheAssociation_AndTheCarriedOwner()
    {
        var world = new WorkerWorld();

        (await world.Worker.ProcessAsync(Message(Payload(fallbackCreate: true)), CancellationToken.None)).IsSuccess.Should().BeTrue();

        var created = world.Creates.Should().ContainSingle().Subject;
        created.Association.Should().Be(new DocumentAssociationBind(Column("sprk_matter"), RecordId));
        created.OwningTeamId.Should().Be(SavedTeamId, "a carried OwningTeamId wins (email attachment children match their parent)");
        world.Ownership.Requests.Should().BeEmpty("the carried team is used as is");
        world.Updates.Should().NotBeEmpty().And.OnlyContain(u => CarriesNoAssociation(u.Update) && CarriesNoIndexColumn(u.Update));
    }

    /// <summary>AC-5: with no carried owner, the owner class is asked over the SAME association the create writes.</summary>
    [Fact]
    public async Task Worker_FallbackCreate_WithNoCarriedOwner_AsksTheOwnerOverTheSameAssociationItIsCreatedWith()
    {
        var world = new WorkerWorld();

        var payload = Payload(fallbackCreate: true) with { OwningTeamId = null };
        (await world.Worker.ProcessAsync(Message(payload), CancellationToken.None)).IsSuccess.Should().BeTrue();

        // The document's question, and the artifact records' (same resolver rule): each names the association.
        world.Ownership.Requests.Should().NotBeEmpty()
            .And.OnlyContain(q => NamesTheMatter(q));
        var created = world.Creates.Should().ContainSingle().Subject;
        created.OwningTeamId.Should().Be(RecordOwnershipResolverDouble.DefaultTeamId);
        created.Association!.Field.TargetEntityLogicalName.Should().Be("sprk_matter");
        created.Association.RecordId.Should().Be(RecordId);
    }

    /// <summary>AC-2: each attachment child is filed in its create to the email's record.</summary>
    [Fact]
    public async Task Worker_AttachmentChildren_AreEachCreatedWithTheParentsAssociation()
    {
        var world = new WorkerWorld();

        (await world.Worker.ProcessAsync(Message(Payload(fallbackCreate: false)), CancellationToken.None)).IsSuccess.Should().BeTrue();

        world.Creates.Should().HaveCount(2, "both attachments become child documents")
            .And.OnlyContain(r => r.Association == new DocumentAssociationBind(Column("sprk_matter"), RecordId))
            .And.OnlyContain(r => r.OwningTeamId == SavedTeamId);
        world.Updates.Should().OnlyContain(u => CarriesNoAssociation(u.Update) && CarriesNoIndexColumn(u.Update));
        world.Updates.Where(u => u.Update.ParentDocumentLookup != null).Should().HaveCount(2,
            "each child's file and parent link stay in its update");
    }

    /// <summary>AC-7 on the worker: an account association has no column; the children are created unassociated.</summary>
    [Fact]
    public async Task Worker_AttachmentChildren_OfAnAccountSave_AreCreatedUnassociated()
    {
        var world = new WorkerWorld();

        var payload = Payload(fallbackCreate: false) with { AssociationType = "account" };
        (await world.Worker.ProcessAsync(Message(payload), CancellationToken.None)).IsSuccess.Should().BeTrue();

        world.Creates.Should().HaveCount(2).And.OnlyContain(r => r.Association == null);
        world.Updates.Should().OnlyContain(u => CarriesNoAssociation(u.Update));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static DocumentLinkField Column(string logicalName) => DocumentLinkFields.All.Single(f => f.LogicalName == logicalName);

    private static bool CarriesNoAssociation(UpdateDocumentRequest update) =>
        update.MatterLookup is null && update.ProjectLookup is null && update.InvoiceLookup is null
        && update.WorkAssignmentLookup is null && update.EventLookup is null && update.TodoLookup is null
        && update.ContactLookup is null;

    private static bool CarriesNoIndexColumn(UpdateDocumentRequest update) =>
        update.SearchIndexName is null && update.SearchIndexed is null && update.SearchIndexedOn is null
        && update.SearchIndexQueuedOn is null && update.SearchIndexCompletedOn is null;

    private static bool NamesTheMatter(RecordOwnershipContext question) => Named(question).Contains(("sprk_matter", RecordId));

    private static IEnumerable<(string Table, Guid Id)> Named(RecordOwnershipContext question)
    {
        if (question.TargetEntityLogicalName is { } table && question.TargetRecordId is { } id)
            yield return (table, id);
        foreach (var parent in question.Parents)
            yield return (parent.EntityLogicalName, parent.RecordId);
    }

    private static SaveRequest DocumentSave(string? entityType) => new()
    {
        ContentType = SaveContentType.Document,
        TargetEntity = entityType is null ? null : new SaveEntityReference { EntityType = entityType, EntityId = RecordId },
        Document = new DocumentMetadata { FileName = "Brief.docx", Title = "Brief" },
    };

    /// <summary>The save's persistence with the Dataverse document seam recorded (the bare test constructor, ADR-038 A2).</summary>
    private sealed class PersistenceWorld
    {
        private readonly OfficeDocumentPersistence _persistence;

        public PersistenceWorld()
        {
            var documents = new Mock<IDocumentDataverseService>();
            documents.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateDocumentRequest r, CancellationToken _) =>
                {
                    CreateAttempts.Add(r);
                    // Dataverse's Create with a supplied primary key that already exists: a duplicate-key fault, no write.
                    if (r.Id is { } id && ExistingIds.Contains(id))
                        throw new InvalidOperationException("Cannot insert duplicate key (0x80040237).");
                    Creates.Add(r);
                    return (r.Id ?? Guid.NewGuid()).ToString();
                });
            documents.Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .Callback<string, UpdateDocumentRequest, CancellationToken>((id, u, _) => Updates.Add((id, u)))
                .Returns(Task.CompletedTask);

            var dedup = new Mock<ContentDedupDetector>(MockBehavior.Loose, null!, null!, null!, NullLogger<ContentDedupDetector>.Instance);

            _persistence = new OfficeDocumentPersistence(
                documents.Object, dedup.Object,
                new LoggerFactory(new ILoggerProvider[] { Logs }).CreateLogger<OfficeDocumentPersistence>());
        }

        public HashSet<Guid> ExistingIds { get; } = new();
        public List<CreateDocumentRequest> CreateAttempts { get; } = new();
        public List<CreateDocumentRequest> Creates { get; } = new();
        public List<(string Id, UpdateDocumentRequest Update)> Updates { get; } = new();
        public RecordingLoggerProvider Logs { get; } = new();

        public Task SaveAsync(SaveRequest request, Guid? preAssignedDocumentId = null) =>
            _persistence.CreateDocumentWithSpePointersAsync(
                request, "drive1", "item1", "https://spe/web", "Brief.docx", "Brief", 100, "user-oid", CancellationToken.None,
                preAssignedDocumentId ?? Guid.NewGuid(),
                owningTeamId: SavedTeamId,
                // The identity the save's owner question was asked with: no canonical, so no link (and no detector call).
                editableContentIdentity: new EditableContentIdentity(CanonicalHash: "h", CanonicalDocumentId: null));
    }

    private static UploadFinalizationPayload Payload(bool fallbackCreate) => new()
    {
        ContentType = SaveContentType.Email,
        AssociationType = "matter",
        AssociationId = RecordId,
        ContainerId = "b!drive",
        TempFileLocation = "spe://b!drive/item-eml",
        FileName = "Status.eml",
        FileSize = 2048,
        DocumentId = fallbackCreate ? null : Guid.Parse("030b030b-0000-4000-8000-0000000000d0"),
        OwningTeamId = SavedTeamId,
        TriggerAiProcessing = false,
        RelatedCommunicationId = Guid.Parse("030b030b-0000-4000-8000-0000000000c0"),
        EmailMetadata = new EmailArtifactPayload
        {
            Subject = "Status",
            SenderEmail = "counsel@test.com",
            InternetMessageId = "<CAF0-030b@mail.example.com>",
            HasAttachments = !fallbackCreate,
        },
    };

    private static OfficeJobMessage Message(UploadFinalizationPayload payload) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = OfficeJobType.UploadFinalization,
        CorrelationId = "corr-030b",
        IdempotencyKey = $"idem-030b-{Guid.NewGuid():N}",
        UserId = "5a5a5a5a-0000-4000-8000-00000000cafe",
        Attempt = 1,
        MaxAttempts = 3,
        Payload = JsonSerializer.SerializeToElement(payload),
    };

    /// <summary>The real worker, doubled at its module boundaries (as UploadFinalizationEmailCommunicationLinkTests does).</summary>
    private sealed class WorkerWorld
    {
        public WorkerWorld()
        {
            var documents = new Mock<IDocumentDataverseService>();
            documents
                .Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateDocumentRequest request, CancellationToken _) =>
                {
                    lock (Creates)
                    {
                        Creates.Add(request);
                    }

                    return Guid.NewGuid().ToString();
                });
            documents
                .Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .Callback<string, UpdateDocumentRequest, CancellationToken>((id, u, _) =>
                {
                    lock (Updates)
                    {
                        Updates.Add((id, u));
                    }
                })
                .Returns(Task.CompletedTask);

            var graphClientFactory = Mock.Of<IGraphClientFactory>();
            var spe = new Mock<SpeFileStore>(
                MockBehavior.Loose,
                new ContainerOperations(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<ILogger<ContainerOperations>>()),
                new DriveItemOperations(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<ILogger<DriveItemOperations>>()),
                new UploadSessionManager(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
                new UserOperations(graphClientFactory, Mock.Of<ILogger<UserOperations>>()),
                null!);
            spe.Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(new byte[] { 1, 2, 3 }));
            spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string path, Stream _, CancellationToken _) => new FileHandleDto(
                    $"item-{Guid.NewGuid():N}", path, null, 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false,
                    $"https://contoso.sharepoint.com/{path}"));

            var converter = new Mock<IEmailToEmlConverter>();
            converter.Setup(c => c.ExtractAttachments(It.IsAny<Stream>())).Returns(() => new[]
            {
                Attachment("Settlement terms.pdf"),
                Attachment("Exhibit A.pdf"),
            });

            var services = new ServiceCollection();
            services.AddScoped(_ => spe.Object);
            services.AddScoped(_ => converter.Object);
            services.AddScoped(_ => new AttachmentFilterService(
                Options.Create(new EmailProcessingOptions()), NullLogger<AttachmentFilterService>.Instance));
            services.AddScoped(_ => Mock.Of<IPostUploadIndexingEnqueuer>());
            var provider = services.BuildServiceProvider();

            var jobSubmission = new Mock<JobSubmissionService>(
                MockBehavior.Loose,
                Options.Create(new ServiceBusOptions()),
                NullLogger<JobSubmissionService>.Instance,
                new Mock<ServiceBusClient>().Object);
            jobSubmission
                .Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            Worker = new UploadFinalizationWorker(
                NullLogger<UploadFinalizationWorker>.Instance,
                new InMemoryTenantCache(),
                new Mock<ServiceBusClient>().Object,
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new ServiceBusOptions()),
                Options.Create(new GraphOptions()),
                documents.Object,
                Mock.Of<IProcessingJobService>(),
                jobSubmission.Object,
                new ConfigurationBuilder().Build(),
                Ownership);
        }

        public UploadFinalizationWorker Worker { get; }
        public RecordOwnershipResolverDouble Ownership { get; } = new();
        public List<CreateDocumentRequest> Creates { get; } = new();
        public List<(string Id, UpdateDocumentRequest Update)> Updates { get; } = new();

        private static EmailAttachmentInfo Attachment(string fileName)
        {
            var bytes = new byte[20_000];
            return new EmailAttachmentInfo
            {
                AttachmentId = Guid.NewGuid(),
                FileName = fileName,
                MimeType = "application/pdf",
                Content = new MemoryStream(bytes),
                SizeBytes = bytes.Length,
            };
        }
    }
}
