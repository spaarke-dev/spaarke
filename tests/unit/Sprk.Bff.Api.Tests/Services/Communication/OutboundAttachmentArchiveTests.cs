using System.Security.Claims;
using System.Text;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Regression (spaarkeai-word-add-in-r1 task 097, owner option A of 2026-10-06): a send with <c>ArchiveToSpe</c> archives
/// each attachment the way the INBOUND archive does — its bytes are saved to SharePoint Embedded as a NEW file in the
/// communication's container, and the archived <c>sprk_document</c> points at THAT file. It used to store the source
/// <c>sprk_document</c> GUID as the Graph item id and the archive container as the drive, a pointer that named no file.
/// Pointing it at the source document's file instead would make two rows share one file, and deleting or relocating
/// the archived row would then delete the original's file — so every archived row must get its own upload.
/// </summary>
/// <remarks>
/// Module-boundary doubles only (ADR-038 §4): the channel sender seam (<see cref="ICommunicationChannelSender"/>),
/// the <see cref="SpeFileStore"/> facade's virtual download / upload / delete, the document read
/// (<see cref="IDocumentDataverseService"/>), the Dataverse writer (<see cref="IGenericEntityService"/>) and the job
/// queue. File metadata comes from the real <see cref="GraphMetadataCache"/> over an in-memory cache, so no Graph
/// transport is touched (no <c>HttpMessageHandler</c>). The communication container decision is the REAL
/// <c>CommunicationContainerResolver</c> (no secure regarding → <c>Communication:ArchiveContainerId</c>) and the
/// document-pointer check on the download runs for real (<see cref="SpeScopeFactoryStub"/>).
/// </remarks>
public class OutboundAttachmentArchiveTests
{
    private const string ArchiveContainerId = "drive-archive";
    private const string RootBusinessUnitContainer = "drive-bu-root";
    private const int EmailAttachmentSourceType = 659490004;

    private sealed record SourceDocument(Guid Id, string DriveId, string ItemId, string Name, byte[] Content);

    private sealed record Upload(string DriveId, string Path, byte[] Content, string ReturnedItemId);

    /// <summary>Everything the send wrote, read back by the assertions.</summary>
    private sealed class Harness
    {
        public required CommunicationService Sut { get; init; }
        public required Mock<SpeFileStore> Spe { get; init; }
        public required List<(Entity Entity, Guid Id)> Created { get; init; }
        public required List<Upload> Uploads { get; init; }
        public required List<JobContract> Jobs { get; init; }
        public required RecordOwnershipResolverDouble Ownership { get; init; }

        public Guid CommunicationId => Created.Single(c => c.Entity.LogicalName == "sprk_communication").Id;

        public List<(Entity Entity, Guid Id)> ArchivedAttachments => Created
            .Where(c => c.Entity.LogicalName == "sprk_document"
                        && c.Entity.GetAttributeValue<OptionSetValue>("sprk_sourcetype")?.Value == EmailAttachmentSourceType)
            .ToList();
    }

    private static SourceDocument Doc(string drive, string item, string name, byte[]? content = null)
        => new(Guid.NewGuid(), drive, item, name, content ?? Encoding.UTF8.GetBytes($"bytes-of-{item}"));

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task SendAsync_WithArchiveToSpe_SavesEachAttachmentAsItsOwnNewFile_AndTheRowPointsAtIt(SendMode sendMode)
    {
        // Arrange — two attachments whose files live in two different business-unit drives.
        var contract = Doc("drive-bu-north", "item-contract", "contract.docx");
        var exhibit = Doc("drive-bu-south", "item-exhibit", "exhibit.pdf");
        var h = await BuildAsync(contract, exhibit);

        // Act
        var response = await h.Sut.SendAsync(Request(sendMode, contract, exhibit), sendMode == SendMode.User ? UserContext() : null);

        // Assert — the send archived.
        response.ArchivedDocumentId.Should().NotBeNull(
            "the .eml archive must succeed for the attachments to archive (warning: {0})", response.ArchivalWarning);

        // ONE new upload per attachment, carrying exactly the bytes that were sent, into the communication's container.
        var attachmentUploads = AttachmentUploads(h, contract, exhibit);
        attachmentUploads.Should().HaveCount(2, "each attachment is saved to SharePoint Embedded once, as its own file");
        attachmentUploads.Select(u => u.Content).Should().BeEquivalentTo(new[] { contract.Content, exhibit.Content });
        attachmentUploads.Should().OnlyContain(u => u.DriveId == ArchiveContainerId,
            "a non-secure communication's content goes to Communication:ArchiveContainerId, the .eml's container");
        attachmentUploads.Should().OnlyContain(u => u.Path.StartsWith($"{h.CommunicationId:N}_"),
            "every archive upload is named {communicationId:N}_… (task 166's archive-path rule reads that prefix)");

        // Each archived row points at the file ITS upload returned — never the source file, never a sprk_document id.
        var archived = h.ArchivedAttachments;
        archived.Should().HaveCount(2);
        archived.Select(a => (Drive: a.Entity.GetAttributeValue<string>("sprk_graphdriveid"), Item: a.Entity.GetAttributeValue<string>("sprk_graphitemid")))
            .Should().BeEquivalentTo(attachmentUploads.Select(u => (Drive: u.DriveId, Item: u.ReturnedItemId)),
                "the archived document points at the NEW file saved for it");
        foreach (var source in new[] { contract, exhibit })
        {
            archived.Should().NotContain(a => a.Entity.GetAttributeValue<string>("sprk_graphitemid") == source.ItemId,
                "two document rows must never share the source document's file");
            archived.Should().NotContain(a => a.Entity.GetAttributeValue<string>("sprk_graphitemid") == source.Id.ToString(),
                "a sprk_document id is not a Graph item id");
        }

        // Associated to the communication, owned like it (task 146), and profiled.
        archived.Should().OnlyContain(a =>
            a.Entity.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id == h.CommunicationId);
        archived.Should().OnlyContain(a =>
            a.Entity.GetAttributeValue<EntityReference>("ownerid") != null
            && a.Entity.GetAttributeValue<EntityReference>("ownerid").Id == RecordOwnershipResolverDouble.DefaultTeamId);
        h.Jobs.Select(j => j.SubjectId).Should().Contain(archived.Select(a => a.Id.ToString()),
            "the Document Profile job is enqueued for each archived attachment, as before");

        // The source documents' files are untouched: nothing written to their drives, nothing deleted.
        h.Uploads.Should().NotContain(u => u.DriveId == contract.DriveId || u.DriveId == exhibit.DriveId);
        h.Spe.Verify(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_WithArchiveToSpe_LargeAttachment_IsSavedWholeThroughTheSameUploadAsInbound()
    {
        // Arrange — a 5 MB attachment (over the R1-era 4 MB "small upload" line). Inbound archiving saves every
        // attachment through SpeFileStore.UploadSmallAsync (Graph's simple PUT, good to 250 MB; the app-only upload
        // session was deleted 2026-08-27), and the send caps all attachments at 35 MB — so the copy takes that path too.
        var large = Doc("drive-bu-north", "item-large", "deposition.pdf", new byte[5 * 1024 * 1024 + 17]);
        new Random(97).NextBytes(large.Content);
        var h = await BuildAsync(large);

        // Act
        var response = await h.Sut.SendAsync(Request(SendMode.User, large), UserContext());

        // Assert
        response.ArchivedDocumentId.Should().NotBeNull("(warning: {0})", response.ArchivalWarning);
        var upload = AttachmentUploads(h, large).Should().ContainSingle().Subject;
        upload.Content.Should().Equal(large.Content, "the whole file is saved, byte for byte");
        var row = h.ArchivedAttachments.Should().ContainSingle().Subject.Entity;
        row.GetAttributeValue<string>("sprk_graphitemid").Should().Be(upload.ReturnedItemId);
        row.GetAttributeValue<string>("sprk_graphdriveid").Should().Be(upload.DriveId);
    }

    [Fact]
    public async Task SendAsync_WithArchiveToSpe_TwoAttachmentsWithOneName_GetTwoFiles_AndUploadsNeverReplace()
    {
        // Arrange — two different documents both called "scan.pdf" (image001.png is the inbound twin of this case).
        var first = Doc("drive-bu-north", "item-scan-1", "scan.pdf");
        var second = Doc("drive-bu-south", "item-scan-2", "scan.pdf");
        var h = await BuildAsync(first, second);

        // Act
        var response = await h.Sut.SendAsync(Request(SendMode.SharedMailbox, first, second));

        // Assert — two distinct paths, two distinct files, two rows pointing at different files.
        response.ArchivedDocumentId.Should().NotBeNull("(warning: {0})", response.ArchivalWarning);
        var uploads = AttachmentUploads(h, first, second);
        uploads.Should().HaveCount(2, "each attachment is saved as its own file");
        uploads.Select(u => u.Path).Should().OnlyHaveUniqueItems("a repeated name must not overwrite the first file");
        h.ArchivedAttachments.Select(a => a.Entity.GetAttributeValue<string>("sprk_graphitemid"))
            .Should().BeEquivalentTo(uploads.Select(u => u.ReturnedItemId),
                "each row points at its own new file, so the two rows never share one");

        // And no archive upload may replace an existing file (the .eml keeps its historical replace semantics).
        h.Spe.Verify(s => s.UploadSmallAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
            It.Is<ConflictBehavior>(b => b != ConflictBehavior.Fail), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── owner decision 2026-10-06: "keep the copy as protected as the original" ─────────────────────────────────

    [Fact]
    public async Task SendAsync_WithArchiveToSpe_SecureSource_NonSecureCommunication_CopyGoesToTheSourcesSecureContainer()
    {
        // Arrange — the source is filed to a SECURE matter and its file lives in that matter's own container; the email
        // regards nothing secure, so its own content (the .eml) goes to the shared archive container.
        var secureMatter = Guid.NewGuid();
        var source = Doc("drive-secure-m1", "item-privileged", "privileged-memo.docx");
        var h = await BuildAsync([source], (world, _) =>
        {
            SecureMatter(world, secureMatter, "drive-secure-m1");
            FiledTo(world, source, secureMatter);
        });

        // Act
        var response = await h.Sut.SendAsync(Request(SendMode.User, source), UserContext());

        // Assert — the send succeeded and archived.
        response.ArchivedDocumentId.Should().NotBeNull("(warning: {0})", response.ArchivalWarning);

        // The copy is in the SOURCE's secure container, never the shared archive the communication uses.
        var upload = AttachmentUploads(h, source).Should().ContainSingle().Subject;
        upload.DriveId.Should().Be("drive-secure-m1", "a secure source's bytes never go to a less-protected container");

        // The row points at the copy, stays associated to the communication, and carries the secure record that put it
        // there — so its derived container, the pointer check and the secure-if-any owner all see the secure matter.
        var row = h.ArchivedAttachments.Should().ContainSingle().Subject.Entity;
        row.GetAttributeValue<string>("sprk_graphdriveid").Should().Be("drive-secure-m1");
        row.GetAttributeValue<string>("sprk_graphitemid").Should().Be(upload.ReturnedItemId);
        row.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id.Should().Be(h.CommunicationId);
        row.GetAttributeValue<EntityReference>("sprk_matter").Should().NotBeNull();
        row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(secureMatter);

        // Owned by task 146's rule over the copy's own parents (the secure matter primary), for the person who sent it.
        h.Ownership.Requests.Should().Contain(c =>
            c.TargetEntityLogicalName == "sprk_matter" && c.TargetRecordId == secureMatter
            && c.Parents.Any(p => p.EntityLogicalName == "sprk_matter" && p.RecordId == secureMatter)
            && c.RequestedBy != null);
        row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(RecordOwnershipResolverDouble.DefaultTeamId);
    }

    [Fact]
    public async Task SendAsync_WithArchiveToSpe_NonSecureSource_CopyGoesToTheCommunicationsContainer()
    {
        // Arrange — the source is filed to an ORDINARY matter (its container is the business unit's); the communication
        // regards nothing secure.
        var ordinaryMatter = Guid.NewGuid();
        var source = Doc("drive-bu-north", "item-ordinary", "engagement-letter.pdf");
        var h = await BuildAsync([source], (world, _) =>
        {
            world.Rows[("sprk_matter", ordinaryMatter)] = new Entity("sprk_matter", ordinaryMatter)
            {
                ["sprk_issecure"] = false,
                ["owningbusinessunit"] = new EntityReference("businessunit", TestRecordContainerResolver.PointerWorldRootBusinessUnit),
            };
            FiledTo(world, source, ordinaryMatter);
        });

        // Act
        var response = await h.Sut.SendAsync(Request(SendMode.SharedMailbox, source));

        // Assert — option A unchanged: the communication's container; the row carries no record link of its own.
        response.ArchivedDocumentId.Should().NotBeNull("(warning: {0})", response.ArchivalWarning);
        var upload = AttachmentUploads(h, source).Should().ContainSingle().Subject;
        upload.DriveId.Should().Be(ArchiveContainerId, "a non-secure source's copy goes where the communication's content goes");
        var row = h.ArchivedAttachments.Should().ContainSingle().Subject.Entity;
        row.GetAttributeValue<string>("sprk_graphdriveid").Should().Be(ArchiveContainerId);
        row.GetAttributeValue<EntityReference>("sprk_matter").Should().BeNull();
        row.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id.Should().Be(h.CommunicationId);
    }

    [Fact]
    public async Task SendAsync_WithArchiveToSpe_SourceSecurityCannotBeDetermined_ThatAttachmentIsNotArchived_OthersAre()
    {
        // Arrange — the second source is filed to a matter whose row cannot be read, so whether it is secure cannot be
        // determined. (The send's own pointer check does not read that link, so the email itself is sent.)
        var unreadableMatter = Guid.NewGuid();
        var ordinary = Doc("drive-bu-north", "item-ok", "agenda.pdf");
        var undetermined = Doc("drive-bu-south", "item-undetermined", "board-minutes.pdf");
        var h = await BuildAsync([ordinary, undetermined], (world, _) => FiledTo(world, undetermined, unreadableMatter));

        // Act
        var response = await h.Sut.SendAsync(Request(SendMode.User, ordinary, undetermined), UserContext());

        // Assert — the send succeeded and archived its .eml.
        response.ArchivedDocumentId.Should().NotBeNull("(warning: {0})", response.ArchivalWarning);

        // Fail closed for THAT attachment only: no file anywhere, no row — never the shared archive container.
        AttachmentUploads(h, undetermined).Should().BeEmpty(
            "a source whose security cannot be determined is not copied anywhere");
        h.ArchivedAttachments.Should().ContainSingle("only the attachment whose source is known non-secure is archived")
            .Which.Entity.GetAttributeValue<string>("sprk_filename").Should().Be(ordinary.Name);
        AttachmentUploads(h, ordinary).Should().ContainSingle().Which.DriveId.Should().Be(ArchiveContainerId);
    }

    [Fact]
    public async Task SendAsync_WithArchiveToSpe_SecureSourceAndSecureCommunicationInDifferentContainers_UsesTheSourcesContainer()
    {
        // Arrange — the email regards secure matter M2 (its .eml goes to M2's container); the attachment's source belongs
        // to a DIFFERENT secure matter, M1.
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        var source = Doc("drive-secure-m1", "item-m1", "m1-strategy.docx");
        var h = await BuildAsync([source], (world, communicationId) =>
        {
            SecureMatter(world, m1, "drive-secure-m1");
            SecureMatter(world, m2, "drive-secure-m2");
            FiledTo(world, source, m1);
            world.Rows[("sprk_communication", communicationId)] = new Entity("sprk_communication", communicationId)
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", m2),
            };
        }, communicationFromWorld: true);

        // Act
        var response = await h.Sut.SendAsync(Request(SendMode.SharedMailbox, source));

        // Assert — the communication's own content went to M2's container ...
        response.ArchivedDocumentId.Should().NotBeNull("(warning: {0})", response.ArchivalWarning);
        h.Uploads.Should().Contain(u => u.DriveId == "drive-secure-m2" && u.Path.EndsWith(".eml", StringComparison.Ordinal),
            "the .eml follows the communication's secure regarding");

        // ... but the attachment copy is in the SOURCE's container: at least as protected as the original.
        var upload = AttachmentUploads(h, source).Should().ContainSingle().Subject;
        upload.DriveId.Should().Be("drive-secure-m1");
        var row = h.ArchivedAttachments.Should().ContainSingle().Subject.Entity;
        row.GetAttributeValue<string>("sprk_graphdriveid").Should().Be("drive-secure-m1");
        row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(m1);
        row.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id.Should().Be(h.CommunicationId,
            "the copy stays associated to the communication");
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static SendCommunicationRequest Request(SendMode sendMode, params SourceDocument[] documents) => new()
    {
        To = new[] { "recipient@example.com" },
        Subject = "Draft for review",
        Body = "<p>Attached.</p>",
        BodyFormat = BodyFormat.HTML,
        CommunicationType = CommunicationType.Email,
        SendMode = sendMode,
        ArchiveToSpe = true,
        AttachmentDocumentIds = documents.Select(d => d.Id.ToString()).ToArray(),
    };

    /// <summary>The uploads that carry an attachment's bytes (the .eml is the other upload).</summary>
    private static List<Upload> AttachmentUploads(Harness h, params SourceDocument[] documents)
        => h.Uploads.Where(u => documents.Any(d => d.Content.AsSpan().SequenceEqual(u.Content))).ToList();

    private static Task<Harness> BuildAsync(params SourceDocument[] documents) => BuildAsync(documents, arrange: null);

    /// <param name="documents">The source documents the send attaches.</param>
    /// <param name="arrange">Adds rows to the task 166 document world (secure records, filed documents), given the id the
    /// communication will get.</param>
    /// <param name="communicationFromWorld">The communication's container is decided by the REAL resolver over that world
    /// (its row must be arranged), instead of "regards nothing secure".</param>
    private static async Task<Harness> BuildAsync(
        SourceDocument[] documents,
        Action<TestRecordContainerResolver.DocumentPointerWorld, Guid>? arrange,
        bool communicationFromWorld = false)
    {
        // The REAL RecordContainerResolver (task 166) over a Dataverse world: the root business unit stamps
        // RootBusinessUnitContainer and claims every drive-bu-* container; an unmodelled document is unfiled and owned
        // there. It serves the download's pointer check AND the archive's "is the source secure?" question.
        var communicationId = Guid.NewGuid();
        var world = new TestRecordContainerResolver.DocumentPointerWorld
        {
            RootClaims = c => c.StartsWith("drive-bu-", StringComparison.Ordinal),
        };
        world.BusinessUnits[TestRecordContainerResolver.PointerWorldRootBusinessUnit] = (null, RootBusinessUnitContainer);
        arrange?.Invoke(world, communicationId);
        var recordContainerResolver = world.Build();
        var communicationContainerResolver = communicationFromWorld
            ? new CommunicationContainerResolver(
                recordContainerResolver, SecurableRegistry(), Mock.Of<ILogger<CommunicationContainerResolver>>())
            : SpeScopeFactoryStub.NonSecureContainerResolver();

        var options = new CommunicationOptions
        {
            ApprovedSenders = new[]
            {
                new ApprovedSenderConfig { Email = "noreply@contoso.com", DisplayName = "Contoso", IsDefault = true }
            },
            DefaultMailbox = "noreply@contoso.com",
            ArchiveContainerId = ArchiveContainerId,
        };

        // The document read: sprk_document id → its own drive + item.
        var documentService = new Mock<IDocumentDataverseService>();
        foreach (var doc in documents)
        {
            documentService
                .Setup(s => s.GetDocumentAsync(doc.Id.ToString(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentEntity
                {
                    Id = doc.Id.ToString(),
                    Name = doc.Name,
                    GraphDriveId = doc.DriveId,
                    GraphItemId = doc.ItemId,
                });
        }

        // File metadata through the real cache-aside read (no Graph transport).
        var metadataCache = new GraphMetadataCache(
            new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())),
            Mock.Of<ILogger<GraphMetadataCache>>());
        foreach (var doc in documents)
        {
            await metadataCache.SetFileMetadataAsync(doc.DriveId, doc.ItemId, new FileHandleDto(
                Id: doc.ItemId, Name: doc.Name, ParentId: null, Size: doc.Content.Length,
                CreatedDateTime: DateTimeOffset.UnixEpoch, LastModifiedDateTime: DateTimeOffset.UnixEpoch,
                ETag: null, IsFolder: false, WebUrl: null));
        }

        var spe = BuildSpeFileStore(metadataCache, documents, out var uploads);

        // The Dataverse writer: capture every create with the id it was given.
        var created = new List<(Entity Entity, Guid Id)>();
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveAsync("sprk_document", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string entity, Guid id, string[] columns, CancellationToken ct) =>
                world.EntityService!.RetrieveAsync(entity, id, columns, ct));
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity e, CancellationToken _) =>
            {
                var id = e.LogicalName == "sprk_communication" ? communicationId : Guid.NewGuid();
                created.Add((e, id));
                return id;
            });

        // The email channel seam: the provider send succeeds.
        var sender = new Mock<ICommunicationChannelSender>();
        sender.SetupGet(s => s.SupportedType).Returns(CommunicationType.Email);
        sender
            .Setup(s => s.SendAsync(It.IsAny<ChannelSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelSendRequest r, CancellationToken _) => new ChannelSendResult { FromAddress = r.FromAddress });

        var dispatcher = new CommunicationChannelDispatcher(
            new[] { sender.Object },
            new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>())) });

        var accountService = new CommunicationAccountService(
            Mock.Of<IDataverseService>(),
            Mock.Of<IDataverseService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options),
            accountService,
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        var jobs = new List<JobContract>();
        var ownership = new RecordOwnershipResolverDouble();
        var sut = new CommunicationService(
            dispatcher,
            senderValidator,
            Mock.Of<ICommunicationDataverseService>(),
            entityService.Object,
            documentService.Object,
            accountService,
            BuildJobQueue(jobs),
            Mock.Of<ICommunicationEnrichmentService>(),
            Microsoft.Extensions.Options.Options.Create(options),
            CoreAncestorResolverFixtures.Inert(),
            ownership,
            Mock.Of<ILogger<CommunicationService>>(),
            scopeFactory: SpeScopeFactoryStub.Create(spe.Object, communicationContainerResolver, recordContainerResolver));

        // The capture lists are shared by reference: the SUT writes into them during the act.
        return new Harness
        {
            Sut = sut, Spe = spe, Created = created, Uploads = uploads, Jobs = jobs, Ownership = ownership,
        };
    }

    /// <summary>The securable roots — non-empty, so the communication resolver does not refuse.</summary>
    private static ISecurableEntityRegistry SecurableRegistry()
    {
        var registry = new Mock<ISecurableEntityRegistry>();
        registry.Setup(r => r.GetSecurableEntitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.Ordinal) { "sprk_project", "sprk_matter", "sprk_workassignment" });
        return registry.Object;
    }

    /// <summary>A SECURE matter owning <paramref name="container"/>: its row, and the container's claimant.</summary>
    private static void SecureMatter(TestRecordContainerResolver.DocumentPointerWorld world, Guid matterId, string container)
    {
        world.Rows[("sprk_matter", matterId)] = new Entity("sprk_matter", matterId)
        {
            ["sprk_issecure"] = true,
            ["sprk_containerid"] = container,
            ["owningbusinessunit"] = new EntityReference("businessunit", TestRecordContainerResolver.PointerWorldRootBusinessUnit),
        };
        world.SecureClaims[container] = ("sprk_matter", matterId);
    }

    /// <summary>The source document's row, filed to <paramref name="matterId"/> (<c>sprk_matter</c>).</summary>
    private static void FiledTo(TestRecordContainerResolver.DocumentPointerWorld world, SourceDocument document, Guid matterId)
    {
        var row = TestRecordContainerResolver.DocumentPointerWorld.Document(document.Id);
        row["sprk_matter"] = new EntityReference("sprk_matter", matterId);
        world.Rows[("sprk_document", document.Id)] = row;
    }

    /// <summary>
    /// The SPE facade over real operation classes; only its virtual download, uploads and delete are doubled. Every
    /// upload is recorded with the bytes it carried and answered with a NEW item id.
    /// </summary>
    private static Mock<SpeFileStore> BuildSpeFileStore(
        GraphMetadataCache metadataCache, SourceDocument[] documents, out List<Upload> uploads)
    {
        var recorded = new List<Upload>();
        uploads = recorded;
        var gcf = Mock.Of<IGraphClientFactory>();
        var speMock = new Mock<SpeFileStore>(
            MockBehavior.Loose,
            new ContainerOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<ContainerOperations>>()),
            new DriveItemOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<DriveItemOperations>>(), metadataCache),
            new UploadSessionManager(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
            new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()),
            null!);

        speMock
            .Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string driveId, string itemId, CancellationToken _) =>
                (Stream)new MemoryStream(documents.Single(d => d.DriveId == driveId && d.ItemId == itemId).Content));

        FileHandleDto? Record(string driveId, string path, Stream content)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            var itemId = $"new-item-{recorded.Count + 1}";
            recorded.Add(new Upload(driveId, path, ms.ToArray(), itemId));
            return new FileHandleDto(
                Id: itemId, Name: path, ParentId: null, Size: ms.Length,
                CreatedDateTime: DateTimeOffset.UnixEpoch, LastModifiedDateTime: DateTimeOffset.UnixEpoch,
                ETag: null, IsFolder: false, WebUrl: null, DriveId: driveId);
        }

        speMock
            .Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string driveId, string path, Stream content, CancellationToken _) => Record(driveId, path, content));
        speMock
            .Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string driveId, string path, Stream content, ConflictBehavior _, CancellationToken _) => Record(driveId, path, content));

        return speMock;
    }

    private static JobSubmissionService BuildJobQueue(List<JobContract> submitted)
    {
        var sbOptions = new Mock<IOptions<ServiceBusOptions>>();
        sbOptions.Setup(o => o.Value).Returns(new ServiceBusOptions
        {
            QueueName = "test-jobs",
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        });

        var jobs = new Mock<JobSubmissionService>(
            MockBehavior.Loose,
            sbOptions.Object,
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<ServiceBusClient>().Object);
        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback((JobContract job, CancellationToken _) => submitted.Add(job))
            .Returns(Task.CompletedTask);
        return jobs.Object;
    }

    /// <summary>A signed-in user (User send mode resolves the sender from these claims).</summary>
    private static HttpContext UserContext() => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("preferred_username", "author@contoso.com"),
            new Claim("oid", Guid.NewGuid().ToString()),
        }, "Test")),
    };
}
