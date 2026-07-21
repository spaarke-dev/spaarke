using System.Net.Http;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using MimeKit;
using Moq;
using Azure.Messaging.ServiceBus;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of the on-demand "Save to SharePoint" archive path (<see cref="CommunicationService.ArchiveExistingAsync"/>)
/// after the UAT #4/#5 remediation (task 092). Three protected contracts:
/// <list type="number">
///   <item>the archived .eml embeds the communication's attachments (faithful original — UAT #4a);</item>
///   <item>the archive Document display name carries the <c>.eml</c> extension (UAT #4b);</item>
///   <item>the on-demand path enqueues Document Profile (AppOnlyDocumentAnalysis) for the .eml (UAT #5).</item>
/// </list>
/// Module-boundary mocks only (ADR-038 §4): the <see cref="SpeFileStore"/> facade (download/upload) and the
/// <see cref="JobSubmissionService"/> queue seam are the real production boundaries. Assertions are on observable
/// output — the uploaded .eml bytes parsed back through MimeKit, the created Document entity, and the enqueued job.
/// </summary>
public class CommunicationServiceArchiveEmbedTests
{
    private const string CommunicationEntity = "sprk_communication";
    private const string AttachmentEntity = "sprk_communicationattachment";
    private const string DocumentEntity = "sprk_document";

    private static CommunicationOptions Options() => new()
    {
        ApprovedSenders = new[]
        {
            new ApprovedSenderConfig { Email = "noreply@contoso.com", DisplayName = "Contoso", IsDefault = true }
        },
        DefaultMailbox = "noreply@contoso.com",
        ArchiveContainerId = "drive-archive"
    };

    /// <summary>Builds a Mock&lt;SpeFileStore&gt; over real operation classes (mocked interface deps). Only the
    /// two virtual facade methods this path touches — DownloadFileAsync + UploadSmallAsync — are set up.</summary>
    private static Mock<SpeFileStore> BuildSpeMock(
        Func<string, byte[]> downloadBytesForItemId,
        Action<byte[]> captureUploadedEml)
    {
        var gcf = Mock.Of<IGraphClientFactory>();
        var containerOps = new ContainerOperations(gcf, Mock.Of<ILogger<ContainerOperations>>());
        var driveItemOps = new DriveItemOperations(gcf, Mock.Of<ILogger<DriveItemOperations>>());
        var uploadMgr = new UploadSessionManager(gcf, Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>());
        var userOps = new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>());

        var speMock = new Mock<SpeFileStore>(MockBehavior.Loose, containerOps, driveItemOps, uploadMgr, userOps, null!);

        speMock
            .Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string itemId, CancellationToken _) =>
                (Stream)new MemoryStream(downloadBytesForItemId(itemId)));

        speMock
            .Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string path, Stream content, CancellationToken _) =>
            {
                using var ms = new MemoryStream();
                content.Position = 0;
                content.CopyTo(ms);
                captureUploadedEml(ms.ToArray());
                return (FileHandleDto?)new FileHandleDto(
                    Id: "item-eml", Name: path, ParentId: null, Size: ms.Length,
                    CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                    ETag: null, IsFolder: false, WebUrl: null);
            });

        return speMock;
    }

    private static Mock<JobSubmissionService> BuildJobMock(List<JobContract> enqueued)
    {
        var sbOptions = new Mock<IOptions<ServiceBusOptions>>();
        sbOptions.Setup(o => o.Value).Returns(new ServiceBusOptions
        {
            QueueName = "test-jobs",
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        });

        var jobMock = new Mock<JobSubmissionService>(
            MockBehavior.Loose,
            sbOptions.Object,
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<ServiceBusClient>().Object);

        jobMock
            .Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => enqueued.Add(job))
            .Returns(Task.CompletedTask);

        return jobMock;
    }

    private static CommunicationService BuildSut(
        IGenericEntityService entityService,
        SpeFileStore speFileStore,
        JobSubmissionService jobSubmissionService)
    {
        var options = Options();
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
        var dispatcher = new CommunicationChannelDispatcher(
            Array.Empty<ICommunicationChannelSender>(),
            new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>())) });

        return new CommunicationService(
            dispatcher,
            senderValidator,
            Mock.Of<ICommunicationDataverseService>(),
            entityService,
            Mock.Of<IDocumentDataverseService>(),
            speFileStore,
            accountService,
            jobSubmissionService,
            Mock.Of<ICommunicationEnrichmentService>(),
            Microsoft.Extensions.Options.Options.Create(options),
            Mock.Of<ILogger<CommunicationService>>());
    }

    /// <summary>
    /// Sets up a fresh (not-yet-archived) communication with the given attachments. Captures every created
    /// sprk_document so the test can find the archive .eml Document and inspect it.
    /// </summary>
    private static Mock<IGenericEntityService> BuildEntityService(
        Guid communicationId,
        IReadOnlyList<Entity> attachments,
        List<(Entity Entity, Guid Id)> createdDocuments)
    {
        var entityService = new Mock<IGenericEntityService>();

        var communication = new Entity(CommunicationEntity, communicationId);
        communication["sprk_subject"] = "Quarterly Filing";
        communication["sprk_from"] = "sender@contoso.com";
        communication["sprk_to"] = "recipient@example.com";
        communication["sprk_direction"] = new OptionSetValue(100000000); // Incoming
        // Received emails always carry a Graph message id — reconstructed into the .eml Message-Id header.
        communication["sprk_graphmessageid"] = "AAMkAGI2abc123@contoso.com";

        entityService
            .Setup(s => s.RetrieveAsync(CommunicationEntity, communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(communication);

        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == AttachmentEntity), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>(attachments)));

        // Not yet archived — FindExistingArchiveDocumentAsync returns empty.
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == DocumentEntity), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>()));

        entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity e, CancellationToken _) =>
            {
                var id = Guid.NewGuid();
                createdDocuments.Add((e, id));
                return id;
            });

        entityService
            .Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return entityService;
    }

    private static Entity Attachment(string name, string itemId, string driveId) =>
        new(AttachmentEntity, Guid.NewGuid())
        {
            ["sprk_name"] = name,
            ["sprk_graphitemid"] = itemId,
            ["sprk_graphdriveid"] = driveId,
        };

    private static Entity ArchiveDocument(List<(Entity Entity, Guid Id)> created) =>
        created.Single(d => d.Entity.GetAttributeValue<bool>("sprk_isemailarchive")).Entity;

    private static Guid ArchiveDocumentId(List<(Entity Entity, Guid Id)> created) =>
        created.Single(d => d.Entity.GetAttributeValue<bool>("sprk_isemailarchive")).Id;

    [Fact]
    public async Task ArchiveExistingAsync_WhenCommunicationHasAttachments_EmbedsAllInEml()
    {
        // Arrange — the communication has two SPE-stored attachments.
        var communicationId = Guid.NewGuid();
        var attachments = new[]
        {
            Attachment("contract.pdf", "item-a", "drive-a"),
            Attachment("summary.txt", "item-b", "drive-b"),
        };
        var created = new List<(Entity, Guid)>();
        var entityService = BuildEntityService(communicationId, attachments, created);

        byte[]? uploadedEml = null;
        var speMock = BuildSpeMock(
            downloadBytesForItemId: itemId => Encoding.UTF8.GetBytes($"bytes-of-{itemId}"),
            captureUploadedEml: bytes => uploadedEml = bytes);
        var jobMock = BuildJobMock(new List<JobContract>());

        var sut = BuildSut(entityService.Object, speMock.Object, jobMock.Object);

        // Act
        var result = await sut.ArchiveExistingAsync(communicationId, CancellationToken.None);

        // Assert — the uploaded .eml parses as an RFC-822 message with BOTH attachments embedded.
        result.AlreadyArchived.Should().BeFalse();
        uploadedEml.Should().NotBeNull("the archive .eml must have been uploaded to SPE");

        using var stream = new MemoryStream(uploadedEml!);
        var parsed = MimeMessage.Load(stream);
        var embeddedNames = parsed.Attachments
            .OfType<MimePart>()
            .Select(p => p.FileName)
            .ToArray();

        embeddedNames.Should().BeEquivalentTo(new[] { "contract.pdf", "summary.txt" });
    }

    [Fact]
    public async Task ArchiveExistingAsync_WhenOneAttachmentDownloadFails_StillArchivesWithRemaining()
    {
        // Arrange — two attachments; the first fails to download from SPE (best-effort per NFR-06).
        var communicationId = Guid.NewGuid();
        var attachments = new[]
        {
            Attachment("broken.pdf", "item-bad", "drive-a"),
            Attachment("ok.txt", "item-ok", "drive-b"),
        };
        var created = new List<(Entity, Guid)>();
        var entityService = BuildEntityService(communicationId, attachments, created);

        byte[]? uploadedEml = null;
        var speMock = BuildSpeMock(
            downloadBytesForItemId: itemId => Encoding.UTF8.GetBytes($"bytes-of-{itemId}"),
            captureUploadedEml: bytes => uploadedEml = bytes);
        // Override the download for the "bad" item to throw.
        speMock
            .Setup(s => s.DownloadFileAsync(It.IsAny<string>(), "item-bad", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SPE download failed"));

        var jobMock = BuildJobMock(new List<JobContract>());
        var sut = BuildSut(entityService.Object, speMock.Object, jobMock.Object);

        // Act — must not throw; archival still succeeds.
        var result = await sut.ArchiveExistingAsync(communicationId, CancellationToken.None);

        // Assert — the .eml archived with only the attachment that downloaded.
        result.AlreadyArchived.Should().BeFalse();
        uploadedEml.Should().NotBeNull();

        using var stream = new MemoryStream(uploadedEml!);
        var parsed = MimeMessage.Load(stream);
        parsed.Attachments.OfType<MimePart>().Select(p => p.FileName)
            .Should().BeEquivalentTo(new[] { "ok.txt" });
    }

    [Fact]
    public async Task ArchiveExistingAsync_ArchiveDocumentName_CarriesEmlExtension()
    {
        // Arrange — no attachments; focus on the archive Document naming.
        var communicationId = Guid.NewGuid();
        var created = new List<(Entity, Guid)>();
        var entityService = BuildEntityService(communicationId, Array.Empty<Entity>(), created);

        var speMock = BuildSpeMock(_ => Array.Empty<byte>(), _ => { });
        var jobMock = BuildJobMock(new List<JobContract>());
        var sut = BuildSut(entityService.Object, speMock.Object, jobMock.Object);

        // Act
        await sut.ArchiveExistingAsync(communicationId, CancellationToken.None);

        // Assert — display name (sprk_documentname) carries the .eml extension, not a bare "Archived: {subject}".
        var archiveDoc = ArchiveDocument(created);
        var displayName = archiveDoc.GetAttributeValue<string>("sprk_documentname");
        displayName.Should().EndWith(".eml");
        displayName.Should().NotStartWith("Archived:");
    }

    [Fact]
    public async Task ArchiveExistingAsync_AfterArchivingEml_EnqueuesDocumentProfileForTheEml()
    {
        // Arrange — no attachments, so the ONLY enqueue on this path is for the .eml archive Document.
        var communicationId = Guid.NewGuid();
        var created = new List<(Entity, Guid)>();
        var entityService = BuildEntityService(communicationId, Array.Empty<Entity>(), created);

        var speMock = BuildSpeMock(_ => Array.Empty<byte>(), _ => { });
        var enqueued = new List<JobContract>();
        var jobMock = BuildJobMock(enqueued);
        var sut = BuildSut(entityService.Object, speMock.Object, jobMock.Object);

        // Act
        await sut.ArchiveExistingAsync(communicationId, CancellationToken.None);

        // Assert — a Document-analysis job was enqueued for the archive .eml Document.
        var archiveDocId = ArchiveDocumentId(created);
        enqueued.Should().ContainSingle();
        enqueued[0].JobType.Should().Be(Sprk.Bff.Api.Services.Ai.Jobs.AppOnlyDocumentAnalysisJobHandler.JobTypeName);
        enqueued[0].SubjectId.Should().Be(archiveDocId.ToString());
    }
}
