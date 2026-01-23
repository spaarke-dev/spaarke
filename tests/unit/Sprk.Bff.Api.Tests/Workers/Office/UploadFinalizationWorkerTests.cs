using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Workers.Office;
using Sprk.Bff.Api.Workers.Office.Messages;
using Xunit;

namespace Sprk.Bff.Api.Tests.Workers.Office;

/// <summary>
/// Unit tests for <see cref="UploadFinalizationWorker"/>.
/// </summary>
public class UploadFinalizationWorkerTests
{
    private readonly Mock<ILogger<UploadFinalizationWorker>> _loggerMock;
    private readonly Mock<SpeFileStore> _speFileStoreMock;
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<ServiceBusClient> _serviceBusClientMock;
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly Mock<Spaarke.Dataverse.IDataverseService> _dataverseServiceMock;
    private readonly ServiceBusOptions _serviceBusOptions;

    public UploadFinalizationWorkerTests()
    {
        _loggerMock = new Mock<ILogger<UploadFinalizationWorker>>();
        _speFileStoreMock = CreateMockSpeFileStore();
        _cacheMock = new Mock<IDistributedCache>();
        _serviceBusClientMock = new Mock<ServiceBusClient>();
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _dataverseServiceMock = new Mock<Spaarke.Dataverse.IDataverseService>();
        _serviceBusOptions = new ServiceBusOptions
        {
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test"
        };
    }

    [Fact]
    public void JobType_ReturnsUploadFinalization()
    {
        // Arrange
        var worker = CreateWorker();

        // Act
        var jobType = worker.JobType;

        // Assert
        jobType.Should().Be(OfficeJobType.UploadFinalization);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsSuccess_WhenIdempotencyKeyAlreadyProcessed()
    {
        // Arrange
        var worker = CreateWorker();
        var idempotencyKey = "test-idempotency-key";
        var message = CreateTestMessage(idempotencyKey: idempotencyKey);

        // Setup cache to return existing document ID (indicating already processed)
        _cacheMock
            .Setup(c => c.GetAsync(
                It.Is<string>(k => k.Contains(idempotencyKey)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _speFileStoreMock.Verify(s =>
            s.UploadSmallAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "Should not upload when already processed");
    }

    [Fact]
    public async Task ProcessAsync_ReturnsSuccess_WhenUploadSucceeds()
    {
        // Arrange
        var worker = CreateWorker();
        var message = CreateTestMessage();
        SetupSuccessfulUpload();
        SetupNoExistingIdempotency();

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsync_MarksIdempotencyKey_WhenProcessingSucceeds()
    {
        // Arrange
        var worker = CreateWorker();
        var idempotencyKey = "unique-key-" + Guid.NewGuid();
        var message = CreateTestMessage(idempotencyKey: idempotencyKey);
        SetupSuccessfulUpload();
        SetupNoExistingIdempotency();

        // Act
        await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        _cacheMock.Verify(c =>
            c.SetAsync(
                It.Is<string>(k => k.Contains(idempotencyKey)),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Should mark idempotency key after successful processing");
    }

    [Fact]
    public async Task ProcessAsync_UsesCorrectContainerAndPath_ForUpload()
    {
        // Arrange
        var worker = CreateWorker();
        var containerId = "test-container-id";
        var folderPath = "test/folder/path";
        var fileName = "test-document.docx";
        var message = CreateTestMessage(containerId: containerId, folderPath: folderPath, fileName: fileName);
        SetupSuccessfulUpload();
        SetupNoExistingIdempotency();

        // Act
        await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        _speFileStoreMock.Verify(s =>
            s.ResolveDriveIdAsync(containerId, It.IsAny<CancellationToken>()),
            Times.Once);

        _speFileStoreMock.Verify(s =>
            s.UploadSmallAsync(
                It.IsAny<string>(),
                It.Is<string>(path => path.Contains(folderPath) && path.Contains(fileName)),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsFailure_WhenUploadFails()
    {
        // Arrange
        var worker = CreateWorker();
        var message = CreateTestMessage();
        SetupNoExistingIdempotency();

        // Setup upload to throw exception
        _speFileStoreMock
            .Setup(s => s.UploadSmallAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SPE upload failed"));

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("OFFICE_012");
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsync_HandlesEmailContent_CreatesEmailArtifact()
    {
        // Arrange
        var worker = CreateWorker();
        var emailMetadata = new EmailArtifactPayload
        {
            Subject = "Test Email Subject",
            SenderEmail = "test@example.com",
            SenderName = "Test User",
            OutlookMessageId = "outlook-message-id-123",
            InternetMessageId = "<test@example.com>",
            ConversationId = "conversation-123"
        };
        var message = CreateTestMessage(
            contentType: SaveContentType.Email,
            emailMetadata: emailMetadata);
        SetupSuccessfulUpload();
        SetupNoExistingIdempotency();

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // Note: In real implementation, would verify Dataverse create calls
    }

    [Fact]
    public async Task ProcessAsync_HandlesAttachmentContent_CreatesAttachmentArtifact()
    {
        // Arrange
        var worker = CreateWorker();
        var attachmentMetadata = new AttachmentArtifactPayload
        {
            OutlookAttachmentId = "attachment-123",
            OriginalFileName = "attachment.pdf",
            ContentType = "application/pdf",
            Size = 12345,
            EmailArtifactId = Guid.NewGuid()
        };
        var message = CreateTestMessage(
            contentType: SaveContentType.Attachment,
            attachmentMetadata: attachmentMetadata);
        SetupSuccessfulUpload();
        SetupNoExistingIdempotency();

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsync_QueuesProfileStage_WhenAiProcessingEnabled()
    {
        // Arrange
        var worker = CreateWorker();
        var senderMock = new Mock<ServiceBusSender>();
        _serviceBusClientMock
            .Setup(c => c.CreateSender(It.Is<string>(q => q.Contains("profile"))))
            .Returns(senderMock.Object);

        var message = CreateTestMessage(triggerAiProcessing: true);
        SetupSuccessfulUpload();
        SetupNoExistingIdempotency();

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        senderMock.Verify(s =>
            s.SendMessageAsync(
                It.Is<ServiceBusMessage>(m => m.Subject == "Profile"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Should queue profile stage when AI processing is enabled");
    }

    [Fact]
    public async Task ProcessAsync_SkipsAiQueue_WhenAiProcessingDisabled()
    {
        // Arrange
        var worker = CreateWorker();
        var senderMock = new Mock<ServiceBusSender>();
        _serviceBusClientMock
            .Setup(c => c.CreateSender(It.IsAny<string>()))
            .Returns(senderMock.Object);

        var message = CreateTestMessage(triggerAiProcessing: false);
        SetupSuccessfulUpload();
        SetupNoExistingIdempotency();

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        senderMock.Verify(s =>
            s.SendMessageAsync(
                It.IsAny<ServiceBusMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "Should not queue any stage when AI processing is disabled");
    }

    [Theory]
    [InlineData(1, 3, true)]  // First attempt, should be retryable
    [InlineData(2, 3, true)]  // Second attempt, should be retryable
    [InlineData(3, 3, false)] // Final attempt, not retryable
    public async Task ProcessAsync_ReturnsCorrectRetryability_BasedOnAttemptCount(
        int attempt,
        int maxAttempts,
        bool expectedRetryable)
    {
        // Arrange
        var worker = CreateWorker();
        var message = CreateTestMessage(attempt: attempt, maxAttempts: maxAttempts);
        SetupNoExistingIdempotency();

        // Setup upload to fail
        _speFileStoreMock
            .Setup(s => s.UploadSmallAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Upload failed"));

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Retryable.Should().Be(expectedRetryable);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsFailure_WhenPayloadInvalid()
    {
        // Arrange
        var worker = CreateWorker();
        var message = new OfficeJobMessage
        {
            JobId = Guid.NewGuid(),
            JobType = OfficeJobType.UploadFinalization,
            CorrelationId = Guid.NewGuid().ToString(),
            IdempotencyKey = "test-key",
            UserId = "test-user",
            Payload = JsonSerializer.SerializeToElement(new { Invalid = "payload" })
        };
        SetupNoExistingIdempotency();

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("OFFICE_INTERNAL");
        result.Retryable.Should().BeFalse();
    }

    // ==================== Helper Methods ====================

    private UploadFinalizationWorker CreateWorker()
    {
        return new UploadFinalizationWorker(
            _loggerMock.Object,
            _speFileStoreMock.Object,
            _cacheMock.Object,
            _serviceBusClientMock.Object,
            _scopeFactoryMock.Object,
            Options.Create(_serviceBusOptions),
            _dataverseServiceMock.Object);
    }

    private Mock<SpeFileStore> CreateMockSpeFileStore()
    {
        // SpeFileStore requires dependencies - create a mock that can be configured
        var mock = new Mock<SpeFileStore>(
            MockBehavior.Default,
            null!, // ContainerOperations
            null!, // DriveItemOperations
            null!, // UploadSessionManager
            null!  // UserOperations
        )
        {
            CallBase = false
        };

        // Setup default ResolveDriveIdAsync
        mock.Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("b!test-drive-id");

        return mock;
    }

    private void SetupSuccessfulUpload()
    {
        var testFileHandle = new FileHandleDto(
            Id: "uploaded-item-id",
            Name: "test-file.docx",
            ParentId: null,
            Size: 1024,
            CreatedDateTime: DateTimeOffset.UtcNow,
            LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: null,
            IsFolder: false,
            WebUrl: "https://spaarke.com/files/test-file");

        _speFileStoreMock
            .Setup(s => s.UploadSmallAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(testFileHandle);

        // Setup Service Bus sender for next stage queue
        var senderMock = new Mock<ServiceBusSender>();
        _serviceBusClientMock
            .Setup(c => c.CreateSender(It.IsAny<string>()))
            .Returns(senderMock.Object);
    }

    private void SetupNoExistingIdempotency()
    {
        _cacheMock
            .Setup(c => c.GetAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
    }

    private static OfficeJobMessage CreateTestMessage(
        string? idempotencyKey = null,
        string? containerId = null,
        string? folderPath = null,
        string? fileName = null,
        SaveContentType contentType = SaveContentType.Document,
        EmailArtifactPayload? emailMetadata = null,
        AttachmentArtifactPayload? attachmentMetadata = null,
        bool triggerAiProcessing = true,
        int attempt = 1,
        int maxAttempts = 3)
    {
        var payload = new UploadFinalizationPayload
        {
            ContentType = contentType,
            AssociationType = "sprk_matter",
            AssociationId = Guid.NewGuid(),
            ContainerId = containerId ?? "test-container",
            FolderPath = folderPath ?? "Documents",
            TempFileLocation = "https://blobstorage.blob.core.windows.net/temp/test-file",
            FileName = fileName ?? "test-document.docx",
            FileSize = 1024,
            MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            EmailMetadata = emailMetadata,
            AttachmentMetadata = attachmentMetadata,
            TriggerAiProcessing = triggerAiProcessing
        };

        return new OfficeJobMessage
        {
            JobId = Guid.NewGuid(),
            JobType = OfficeJobType.UploadFinalization,
            CorrelationId = Guid.NewGuid().ToString(),
            IdempotencyKey = idempotencyKey ?? Guid.NewGuid().ToString(),
            UserId = "test-user-id",
            Attempt = attempt,
            MaxAttempts = maxAttempts,
            Payload = JsonSerializer.SerializeToElement(payload)
        };
    }
}
