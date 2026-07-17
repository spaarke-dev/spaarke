using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Communication;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of <see cref="MessageAttachmentMaterializer"/> — the net-new messaging attachment-materialization
/// step (task 070 / FR-14). These protect the load-bearing correctness properties that would regress silently:
/// (1) a valid file materializes into SPE as a governed <c>sprk_document</c> linked to the message via
/// <c>sprk_document.sprk_communication</c> + the <c>sprk_communicationattachment</c> intersection, with the
/// ACS message carrying a REFERENCE (not the binary); (2) CHAT-ATTACHMENT-POLICY.md is enforced BEFORE any
/// upload — oversize + disallowed MIME are rejected with RFC 7807 ProblemDetails and NO SPE upload / no
/// <c>sprk_document</c> occurs; and (3) all SPE access flows through the <see cref="ISpeFileOperations"/>
/// SpeFileStore facade (ADR-007 — no bypass). The SPE facade + canonical <see cref="IGenericEntityService"/>
/// are mocked at the module boundary (no live SPE/Dataverse/ACS is provisioned in R1) — the side effects have
/// no other observable surface.
/// </summary>
public class MessageAttachmentMaterializerTests
{
    private const string DriveId = "b!drive-id-000";
    private static readonly Guid CommunicationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static MaterializeAttachmentRequest ValidRequest(
        string fileName = "brief.pdf",
        string contentType = "application/pdf",
        Stream? content = null) =>
        new()
        {
            CommunicationId = CommunicationId,
            FileName = fileName,
            ContentType = contentType,
            Content = content ?? new MemoryStream(Encoding.UTF8.GetBytes("hello pdf bytes")),
            CorrelationId = "corr-070",
        };

    private static FileHandleDto UploadedHandle(string id = "spe-item-abc", long size = 15) =>
        new(
            Id: id,
            Name: "brief.pdf",
            ParentId: null,
            Size: size,
            CreatedDateTime: DateTimeOffset.UnixEpoch,
            LastModifiedDateTime: DateTimeOffset.UnixEpoch,
            ETag: null,
            IsFolder: false,
            WebUrl: null);

    private static MessageAttachmentMaterializer CreateSut(
        Mock<ISpeFileOperations> spe,
        Mock<IGenericEntityService> generic,
        string? archiveContainerId = DriveId) =>
        new(
            spe.Object,
            generic.Object,
            Options.Create(new CommunicationOptions
            {
                ApprovedSenders = new[] { new ApprovedSenderConfig { Email = "n@x.com", DisplayName = "N" } },
                WebhookNotificationUrl = "https://x/webhook",
                WebhookClientState = "state",
                WebhookSigningKey = "key",
                ArchiveContainerId = archiveContainerId,
            }),
            Mock.Of<ILogger<MessageAttachmentMaterializer>>());

    [Fact]
    public async Task MaterializeAsync_WithValidFile_UploadsToSpeAndCreatesGovernedDocumentAndIntersection()
    {
        var documentId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var created = new List<DataverseEntity>();

        var spe = new Mock<ISpeFileOperations>();
        spe.Setup(s => s.UploadSmallAsync(DriveId, It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UploadedHandle());

        var generic = new Mock<IGenericEntityService>();
        generic.Setup(g => g.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((e, _) => created.Add(e))
            .ReturnsAsync(() => created.Count == 1 ? documentId : attachmentId);

        var sut = CreateSut(spe, generic);

        var result = await sut.MaterializeAsync(ValidRequest());

        result.Succeeded.Should().BeTrue();
        result.Problem.Should().BeNull();

        // Binary uploaded via the SpeFileStore facade exactly once.
        spe.Verify(s => s.UploadSmallAsync(DriveId, It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);

        // Two governed records: sprk_document (message → doc lookup) + sprk_communicationattachment intersection.
        created.Should().HaveCount(2);
        var document = created[0];
        document.LogicalName.Should().Be("sprk_document");
        ((EntityReference)document["sprk_communication"]).Id.Should().Be(CommunicationId);
        document["sprk_graphitemid"].Should().Be("spe-item-abc");
        document["sprk_graphdriveid"].Should().Be(DriveId);

        var intersection = created[1];
        intersection.LogicalName.Should().Be("sprk_communicationattachment");
        ((EntityReference)intersection["sprk_communication"]).Id.Should().Be(CommunicationId);
        ((EntityReference)intersection["sprk_document"]).Id.Should().Be(documentId);
        ((OptionSetValue)intersection["sprk_attachmenttype"]).Value.Should().Be(100000000); // File

        // The ACS message carries a REFERENCE (document + SPE pointers), NOT the binary.
        result.Reference.Should().NotBeNull();
        result.Reference!.DocumentId.Should().Be(documentId);
        result.Reference.CommunicationAttachmentId.Should().Be(attachmentId);
        result.Reference.GraphItemId.Should().Be("spe-item-abc");
        result.Reference.GraphDriveId.Should().Be(DriveId);
    }

    [Fact]
    public async Task MaterializeAsync_WhenOversize_RejectsWithProblemDetailsAndNoUploadOrDocument()
    {
        // 25 MB + 1 byte — over the CHAT-ATTACHMENT-POLICY binary cap.
        var oversize = new MemoryStream(new byte[MessageAttachmentMaterializer.MaxAttachmentSizeBytes + 1]);

        var spe = new Mock<ISpeFileOperations>(MockBehavior.Strict);
        var generic = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var sut = CreateSut(spe, generic);

        var result = await sut.MaterializeAsync(ValidRequest(fileName: "big.pdf", content: oversize));

        result.Succeeded.Should().BeFalse();
        result.Reference.Should().BeNull();
        result.Problem.Should().NotBeNull();
        result.Problem!.Status.Should().Be(413);
        result.Problem.Extensions["errorCode"].Should().Be("ATTACHMENT_TOO_LARGE");

        // Policy runs BEFORE upload — nothing touched SPE or Dataverse (Strict mocks would throw if they did).
        spe.Verify(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        generic.Verify(g => g.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MaterializeAsync_WhenDisallowedMimeType_RejectsWithProblemDetailsAndNoUpload()
    {
        var spe = new Mock<ISpeFileOperations>(MockBehavior.Strict);
        var generic = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var sut = CreateSut(spe, generic);

        // application/zip is outside the 4-type allow-list (text/plain, text/markdown, application/pdf, DOCX).
        var result = await sut.MaterializeAsync(ValidRequest(fileName: "payload.zip", contentType: "application/zip"));

        result.Succeeded.Should().BeFalse();
        result.Reference.Should().BeNull();
        result.Problem.Should().NotBeNull();
        result.Problem!.Status.Should().Be(415);
        result.Problem.Extensions["errorCode"].Should().Be("ATTACHMENT_TYPE_NOT_ALLOWED");

        spe.Verify(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        generic.Verify(g => g.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/markdown")]
    [InlineData("application/pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public async Task MaterializeAsync_WithEachAllowListedMimeType_IsAccepted(string contentType)
    {
        var spe = new Mock<ISpeFileOperations>();
        spe.Setup(s => s.UploadSmallAsync(DriveId, It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UploadedHandle());
        var generic = new Mock<IGenericEntityService>();
        generic.Setup(g => g.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var sut = CreateSut(spe, generic);

        var result = await sut.MaterializeAsync(ValidRequest(fileName: "doc", contentType: contentType));

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task MaterializeAsync_OnSuccess_ReferenceCarriesNoBinaryAndOnlySpeFacadeIsUsed()
    {
        var spe = new Mock<ISpeFileOperations>(MockBehavior.Strict);
        spe.Setup(s => s.UploadSmallAsync(DriveId, It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UploadedHandle(size: 15));
        var generic = new Mock<IGenericEntityService>();
        generic.Setup(g => g.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var sut = CreateSut(spe, generic);

        var result = await sut.MaterializeAsync(ValidRequest());

        // Reference-not-binary: the returned reference exposes SPE pointers + metadata only — there is no
        // binary/byte payload on it, and the type carries no ACS message body. SPE is the store.
        result.Reference.Should().NotBeNull();
        result.Reference!.SizeBytes.Should().Be(15);
        typeof(MessageAttachmentReference).GetProperties()
            .Select(p => p.PropertyType)
            .Should().NotContain(new[] { typeof(Stream), typeof(byte[]) });

        // ADR-007: the ONLY SPE access is the SpeFileStore facade's UploadSmallAsync (Strict mock — any other
        // facade call would throw). No GraphServiceClient / ACS SDK is involved (NFR-04 — none is injected).
        spe.Verify(s => s.UploadSmallAsync(DriveId, It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        spe.VerifyNoOtherCalls();
    }
}
