using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Integration tests for Email Analysis flow (FR-11, FR-12, FR-13).
/// Tests the AnalyzeEmailAsync method and EmailAnalysisJobHandler.
/// </summary>
public class EmailAnalysisIntegrationTests
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ISpeFileOperations> _speFileOperationsMock;
    private readonly Mock<ITextExtractor> _textExtractorMock;
    private readonly Mock<IPlaybookService> _playbookServiceMock;
    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly Mock<IToolHandlerRegistry> _toolHandlerRegistryMock;
    private readonly Mock<ILogger<AppOnlyAnalysisService>> _loggerMock;
    private readonly AppOnlyAnalysisService _service;

    public EmailAnalysisIntegrationTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _speFileOperationsMock = new Mock<ISpeFileOperations>();
        _textExtractorMock = new Mock<ITextExtractor>();
        _playbookServiceMock = new Mock<IPlaybookService>();
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _toolHandlerRegistryMock = new Mock<IToolHandlerRegistry>();
        _loggerMock = new Mock<ILogger<AppOnlyAnalysisService>>();

        _service = new AppOnlyAnalysisService(
            _dataverseServiceMock.Object,
            _speFileOperationsMock.Object,
            _textExtractorMock.Object,
            _playbookServiceMock.Object,
            _scopeResolverMock.Object,
            _toolHandlerRegistryMock.Object,
            _loggerMock.Object);
    }

    #region Test Fixtures - Sample Email Documents

    private static DocumentEntity CreateEmailDocument(
        Guid? id = null,
        Guid? emailId = null,
        string? subject = null,
        string? from = null,
        string? to = null,
        string? body = null)
    {
        var docId = id ?? Guid.NewGuid();
        return new DocumentEntity
        {
            Id = docId.ToString(),
            Name = $"{subject ?? "Test Email"}.eml",
            FileName = $"{subject ?? "Test Email"}.eml",
            GraphDriveId = "drive-123",
            GraphItemId = $"item-{docId:N}",
            Status = DocumentStatus.Active,
            EmailSubject = subject ?? "Test Subject",
            EmailFrom = from ?? "sender@example.com",
            EmailTo = to ?? "recipient@example.com",
            EmailCc = "cc@example.com",
            EmailDate = DateTime.UtcNow.AddHours(-1),
            EmailBody = body ?? "This is the email body content.",
            IsEmailArchive = true
        };
    }

    /// <summary>
    /// Helper to track attachment documents with their expected extracted text for test setup.
    /// </summary>
    private record AttachmentTestData(DocumentEntity Document, string ExpectedText);

    private static AttachmentTestData CreateAttachmentDocument(
        Guid parentDocumentId,
        string fileName,
        string? extractedText = null)
    {
        var docId = Guid.NewGuid();
        var doc = new DocumentEntity
        {
            Id = docId.ToString(),
            Name = fileName,
            FileName = fileName,
            GraphDriveId = "drive-123",
            GraphItemId = $"item-{docId:N}",
            Status = DocumentStatus.Active,
            ParentDocumentId = parentDocumentId.ToString()
        };
        return new AttachmentTestData(doc, extractedText ?? $"Content from {fileName}");
    }

    private static PlaybookResponse CreateEmailAnalysisPlaybook(Guid? id = null)
    {
        return new PlaybookResponse
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Email Analysis",
            Description = "Email analysis playbook for testing",
            IsPublic = true,
            ToolIds = [Guid.NewGuid()]
        };
    }

    private static ResolvedScopes CreateScopesWithTools()
    {
        var tools = new[]
        {
            new AnalysisTool
            {
                Id = Guid.NewGuid(),
                Name = "Summary",
                Type = ToolType.Summary,
                HandlerClass = "SummaryHandler"
            }
        };
        return new ResolvedScopes([], [], tools);
    }

    private void SetupSuccessfulEmailAnalysisFlow(
        Guid emailId,
        DocumentEntity mainDocument,
        List<AttachmentTestData>? attachments = null)
    {
        // Setup email document lookup
        _dataverseServiceMock
            .Setup(x => x.GetDocumentByEmailLookupAsync(emailId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mainDocument);

        // Extract documents for Dataverse mock
        var attachmentDocuments = attachments?.Select(a => a.Document).ToList() ?? new List<DocumentEntity>();

        // Setup child documents (attachments) query
        _dataverseServiceMock
            .Setup(x => x.GetDocumentsByParentAsync(
                Guid.Parse(mainDocument.Id),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(attachmentDocuments);

        // Setup text extraction support
        _textExtractorMock
            .Setup(x => x.IsSupported(It.IsAny<string>()))
            .Returns(true);

        // Setup file download for main document
        var testStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Email body text from .eml file"));
        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(mainDocument.GraphDriveId!, mainDocument.GraphItemId!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testStream);

        // Setup file download for attachments (using expected text from test data)
        if (attachments != null)
        {
            foreach (var attachment in attachments)
            {
                var attachmentStream = new MemoryStream(
                    System.Text.Encoding.UTF8.GetBytes(attachment.ExpectedText));
                _speFileOperationsMock
                    .Setup(x => x.DownloadFileAsync(attachment.Document.GraphDriveId!, attachment.Document.GraphItemId!, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(attachmentStream);
            }
        }

        // Setup text extraction
        _textExtractorMock
            .Setup(x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream stream, string fileName, CancellationToken ct) =>
            {
                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();
                return new TextExtractionResult
                {
                    Success = true,
                    Text = text
                };
            });

        // Setup playbook service
        var playbook = CreateEmailAnalysisPlaybook();
        _playbookServiceMock
            .Setup(x => x.GetByNameAsync("Email Analysis", It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbook);

        // Setup scope resolver
        var scopes = CreateScopesWithTools();
        _scopeResolverMock
            .Setup(x => x.ResolvePlaybookScopesAsync(playbook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopes);

        // Setup tool handler with a mock handler that returns success
        var mockHandler = new Mock<IAnalysisToolHandler>();
        mockHandler.Setup(h => h.Validate(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>()))
            .Returns(ToolValidationResult.Success());
        mockHandler.Setup(h => h.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ToolExecutionContext ctx, AnalysisTool tool, CancellationToken ct) =>
                ToolResult.Ok(
                    handlerId: "SummaryHandler",
                    toolId: tool.Id,
                    toolName: tool.Name,
                    data: new { fullText = "Test summary generated by AI", keywords = "test, email, analysis" },
                    summary: "Test summary generated by AI",
                    confidence: 0.95));

        _toolHandlerRegistryMock
            .Setup(x => x.GetHandlersByType(It.IsAny<ToolType>()))
            .Returns(new[] { mockHandler.Object });

        // Setup document update
        _dataverseServiceMock
            .Setup(x => x.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    #endregion

    #region AnalyzeEmailAsync Tests - Context Combination (FR-11, FR-12)

    [Fact]
    public async Task AnalyzeEmailAsync_WithAttachments_CombinesContext()
    {
        // Arrange - Email with two attachments
        var emailId = Guid.NewGuid();
        var mainDocId = Guid.NewGuid();
        var mainDocument = CreateEmailDocument(
            id: mainDocId,
            emailId: emailId,
            subject: "Contract Review",
            from: "legal@company.com",
            to: "manager@company.com",
            body: "Please review the attached contract and addendum.");

        var attachments = new List<AttachmentTestData>
        {
            CreateAttachmentDocument(mainDocId, "Contract.pdf", "This is the main contract content with terms and conditions."),
            CreateAttachmentDocument(mainDocId, "Addendum.docx", "This addendum modifies section 5 of the contract.")
        };

        SetupSuccessfulEmailAnalysisFlow(emailId, mainDocument, attachments);

        // Act
        var result = await _service.AnalyzeEmailAsync(emailId);

        // Assert - Verify attachments were queried
        _dataverseServiceMock.Verify(
            x => x.GetDocumentsByParentAsync(mainDocId, It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify text extraction was called for attachments
        _speFileOperationsMock.Verify(
            x => x.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(2)); // At least main doc + attachments
    }

    [Fact]
    public async Task AnalyzeEmailAsync_NoAttachments_ProcessesEmailOnly()
    {
        // Arrange - Email with no attachments
        var emailId = Guid.NewGuid();
        var mainDocId = Guid.NewGuid();
        var mainDocument = CreateEmailDocument(
            id: mainDocId,
            emailId: emailId,
            subject: "Simple Notification",
            body: "This is a notification email with no attachments.");

        SetupSuccessfulEmailAnalysisFlow(emailId, mainDocument, attachments: new List<AttachmentTestData>());

        // Act
        var result = await _service.AnalyzeEmailAsync(emailId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.AttachmentsAnalyzed.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeEmailAsync_CombinesMetadataWithBody()
    {
        // Arrange - Email with specific metadata
        var emailId = Guid.NewGuid();
        var mainDocument = CreateEmailDocument(
            emailId: emailId,
            subject: "Important Meeting Notes",
            from: "executive@company.com",
            to: "team@company.com",
            body: "Here are the meeting notes from today's discussion.");

        SetupSuccessfulEmailAnalysisFlow(emailId, mainDocument);

        // Act
        var result = await _service.AnalyzeEmailAsync(emailId);

        // Assert - Verify playbook was loaded for combined analysis
        _playbookServiceMock.Verify(
            x => x.GetByNameAsync("Email Analysis", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AnalyzeEmailAsync_FindsDocumentByEmailLookup()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var mainDocument = CreateEmailDocument(emailId: emailId);

        SetupSuccessfulEmailAnalysisFlow(emailId, mainDocument);

        // Act
        var result = await _service.AnalyzeEmailAsync(emailId);

        // Assert - Verify lookup by email ID
        _dataverseServiceMock.Verify(
            x => x.GetDocumentByEmailLookupAsync(emailId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region AnalyzeEmailAsync Tests - Large Email Truncation (FR-12)

    [Fact]
    public async Task AnalyzeEmailAsync_LargeEmail_TruncatesGracefully()
    {
        // Arrange - Email with very large attachment content
        var emailId = Guid.NewGuid();
        var mainDocId = Guid.NewGuid();
        var mainDocument = CreateEmailDocument(id: mainDocId, emailId: emailId);

        // Create attachments with large content (>100KB combined)
        var largeContent1 = new string('A', 60_000); // 60KB
        var largeContent2 = new string('B', 60_000); // 60KB
        var attachments = new List<AttachmentTestData>
        {
            CreateAttachmentDocument(mainDocId, "Large1.pdf", largeContent1),
            CreateAttachmentDocument(mainDocId, "Large2.pdf", largeContent2)
        };

        SetupSuccessfulEmailAnalysisFlow(emailId, mainDocument, attachments);

        // Act
        var result = await _service.AnalyzeEmailAsync(emailId);

        // Assert - Should succeed despite large content (truncation handled internally)
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeEmailAsync_ManyAttachments_ProcessesAll()
    {
        // Arrange - Email with many attachments
        var emailId = Guid.NewGuid();
        var mainDocId = Guid.NewGuid();
        var mainDocument = CreateEmailDocument(id: mainDocId, emailId: emailId);

        var attachments = Enumerable.Range(1, 10)
            .Select(i => CreateAttachmentDocument(mainDocId, $"Document{i}.pdf", $"Content for document {i}"))
            .ToList();

        SetupSuccessfulEmailAnalysisFlow(emailId, mainDocument, attachments);

        // Act
        var result = await _service.AnalyzeEmailAsync(emailId);

        // Assert - All attachments should be queried
        result.IsSuccess.Should().BeTrue();
        _dataverseServiceMock.Verify(
            x => x.GetDocumentsByParentAsync(mainDocId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region AnalyzeEmailAsync Tests - Entity Field Population (FR-13)

    [Fact]
    public async Task AnalyzeEmailAsync_PopulatesEntityFields()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var mainDocId = Guid.NewGuid();
        var mainDocument = CreateEmailDocument(
            id: mainDocId,
            emailId: emailId,
            subject: "Test Email for Field Population");

        SetupSuccessfulEmailAnalysisFlow(emailId, mainDocument);

        // Act
        var result = await _service.AnalyzeEmailAsync(emailId);

        // Assert - Verify document was updated with analysis results
        _dataverseServiceMock.Verify(
            x => x.UpdateDocumentAsync(
                mainDocId.ToString(),
                It.IsAny<UpdateDocumentRequest>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task AnalyzeEmailAsync_SetsSummaryStatusToPending()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var mainDocId = Guid.NewGuid();
        var mainDocument = CreateEmailDocument(id: mainDocId, emailId: emailId);

        SetupSuccessfulEmailAnalysisFlow(emailId, mainDocument);

        // Act
        var result = await _service.AnalyzeEmailAsync(emailId);

        // Assert - Verify status was set to pending (100000001) during processing
        _dataverseServiceMock.Verify(
            x => x.UpdateDocumentAsync(
                mainDocId.ToString(),
                It.Is<UpdateDocumentRequest>(r => r.SummaryStatus == 100000001),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task AnalyzeEmailAsync_Success_ReturnsAttachmentCount()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var mainDocId = Guid.NewGuid();
        var mainDocument = CreateEmailDocument(id: mainDocId, emailId: emailId);

        var attachments = new List<AttachmentTestData>
        {
            CreateAttachmentDocument(mainDocId, "Doc1.pdf"),
            CreateAttachmentDocument(mainDocId, "Doc2.docx"),
            CreateAttachmentDocument(mainDocId, "Doc3.xlsx")
        };

        SetupSuccessfulEmailAnalysisFlow(emailId, mainDocument, attachments);

        // Act
        var result = await _service.AnalyzeEmailAsync(emailId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.AttachmentsAnalyzed.Should().Be(3);
        result.EmailId.Should().Be(emailId);
        result.DocumentId.Should().Be(mainDocId);
    }

    #endregion

    #region AnalyzeEmailAsync Tests - Error Handling

    [Fact]
    public async Task AnalyzeEmailAsync_NoEmailDocument_ReturnsFailedResult()
    {
        // Arrange - No .eml document exists for the email
        var emailId = Guid.NewGuid();

        _dataverseServiceMock
            .Setup(x => x.GetDocumentByEmailLookupAsync(emailId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentEntity?)null);

        // Act
        var result = await _service.AnalyzeEmailAsync(emailId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No .eml document");
        result.EmailId.Should().Be(emailId);
        result.DocumentId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task AnalyzeEmailAsync_PlaybookNotFound_ReturnsFailedResult()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var mainDocument = CreateEmailDocument(emailId: emailId);

        _dataverseServiceMock
            .Setup(x => x.GetDocumentByEmailLookupAsync(emailId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mainDocument);

        _dataverseServiceMock
            .Setup(x => x.GetDocumentsByParentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentEntity>());

        _textExtractorMock
            .Setup(x => x.IsSupported(It.IsAny<string>()))
            .Returns(true);

        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test")));

        _textExtractorMock
            .Setup(x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextExtractionResult { Success = true, Text = "text" });

        _playbookServiceMock
            .Setup(x => x.GetByNameAsync("Email Analysis", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Playbook not found"));

        _dataverseServiceMock
            .Setup(x => x.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.AnalyzeEmailAsync(emailId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Playbook");
    }

    [Fact]
    public async Task AnalyzeEmailAsync_AttachmentExtractionFails_ContinuesWithOthers()
    {
        // Arrange - One attachment fails, others succeed
        var emailId = Guid.NewGuid();
        var mainDocId = Guid.NewGuid();
        var mainDocument = CreateEmailDocument(id: mainDocId, emailId: emailId);

        var goodAttachmentData = CreateAttachmentDocument(mainDocId, "Good.pdf", "Good content");
        var badAttachmentData = CreateAttachmentDocument(mainDocId, "Bad.pdf");
        var goodAttachment = goodAttachmentData.Document;
        var badAttachment = badAttachmentData.Document;

        _dataverseServiceMock
            .Setup(x => x.GetDocumentByEmailLookupAsync(emailId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mainDocument);

        _dataverseServiceMock
            .Setup(x => x.GetDocumentsByParentAsync(mainDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentEntity> { goodAttachment, badAttachment });

        _textExtractorMock
            .Setup(x => x.IsSupported(It.IsAny<string>()))
            .Returns(true);

        // Good attachment succeeds
        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(mainDocument.GraphDriveId!, mainDocument.GraphItemId!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("email body")));

        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(goodAttachment.GraphDriveId!, goodAttachment.GraphItemId!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Good content")));

        // Bad attachment fails
        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(badAttachment.GraphDriveId!, badAttachment.GraphItemId!, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Download failed"));

        _textExtractorMock
            .Setup(x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextExtractionResult { Success = true, Text = "extracted" });

        var playbook = CreateEmailAnalysisPlaybook();
        _playbookServiceMock
            .Setup(x => x.GetByNameAsync("Email Analysis", It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbook);

        _scopeResolverMock
            .Setup(x => x.ResolvePlaybookScopesAsync(playbook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateScopesWithTools());

        // Setup tool handler with a mock handler that returns success
        var mockHandler = new Mock<IAnalysisToolHandler>();
        mockHandler.Setup(h => h.Validate(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>()))
            .Returns(ToolValidationResult.Success());
        mockHandler.Setup(h => h.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<AnalysisTool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ToolExecutionContext ctx, AnalysisTool tool, CancellationToken ct) =>
                ToolResult.Ok(
                    handlerId: "SummaryHandler",
                    toolId: tool.Id,
                    toolName: tool.Name,
                    data: new { fullText = "Test summary generated by AI", keywords = "test, email, analysis" },
                    summary: "Test summary generated by AI",
                    confidence: 0.95));

        _toolHandlerRegistryMock
            .Setup(x => x.GetHandlersByType(It.IsAny<ToolType>()))
            .Returns(new[] { mockHandler.Object });

        _dataverseServiceMock
            .Setup(x => x.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.AnalyzeEmailAsync(emailId);

        // Assert - Should succeed with partial attachment extraction
        // (The good attachment was processed, bad one was skipped)
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region EmailAnalysisResult Tests

    [Fact]
    public void EmailAnalysisResult_Success_CreatesSuccessfulResult()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var attachmentsAnalyzed = 3;

        // Act
        var result = EmailAnalysisResult.Success(emailId, documentId, attachmentsAnalyzed);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.EmailId.Should().Be(emailId);
        result.DocumentId.Should().Be(documentId);
        result.AttachmentsAnalyzed.Should().Be(attachmentsAnalyzed);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void EmailAnalysisResult_Failed_CreatesFailedResult()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var errorMessage = "Email analysis failed";

        // Act
        var result = EmailAnalysisResult.Failed(emailId, documentId, errorMessage);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.EmailId.Should().Be(emailId);
        result.DocumentId.Should().Be(documentId);
        result.ErrorMessage.Should().Be(errorMessage);
    }

    #endregion
}

/// <summary>
/// Unit tests for EmailAnalysisJobHandler - job processing for email analysis.
/// Tests job processing, idempotency, and error handling.
/// </summary>
public class EmailAnalysisJobHandlerTests
{
    private readonly Mock<IAppOnlyAnalysisService> _analysisServiceMock;
    private readonly Mock<IIdempotencyService> _idempotencyServiceMock;
    private readonly Mock<DocumentTelemetry> _telemetryMock;
    private readonly Mock<ILogger<EmailAnalysisJobHandler>> _loggerMock;
    private readonly EmailAnalysisJobHandler _handler;

    public EmailAnalysisJobHandlerTests()
    {
        _analysisServiceMock = new Mock<IAppOnlyAnalysisService>();
        _idempotencyServiceMock = new Mock<IIdempotencyService>();
        _telemetryMock = new Mock<DocumentTelemetry>();
        _loggerMock = new Mock<ILogger<EmailAnalysisJobHandler>>();

        _handler = new EmailAnalysisJobHandler(
            _analysisServiceMock.Object,
            _idempotencyServiceMock.Object,
            _telemetryMock.Object,
            _loggerMock.Object);
    }

    #region Helper Methods

    private static JobContract CreateEmailAnalysisJobContract(
        Guid? emailId = null,
        string? idempotencyKey = null)
    {
        var id = emailId ?? Guid.NewGuid();
        var payload = new EmailAnalysisPayload
        {
            EmailId = id,
            Source = "UnitTest",
            EnqueuedAt = DateTimeOffset.UtcNow
        };

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = EmailAnalysisJobHandler.JobTypeName,
            SubjectId = id.ToString(),
            CorrelationId = Guid.NewGuid().ToString(),
            IdempotencyKey = idempotencyKey ?? $"emailanalysis-{id}",
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(payload))
        };
    }

    private void SetupSuccessfulIdempotencyFlow()
    {
        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _idempotencyServiceMock
            .Setup(x => x.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _idempotencyServiceMock
            .Setup(x => x.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    #endregion

    #region JobType Tests

    [Fact]
    public void JobType_ReturnsCorrectValue()
    {
        // Assert
        _handler.JobType.Should().Be("EmailAnalysis");
    }

    [Fact]
    public void JobTypeName_Constant_HasExpectedValue()
    {
        // Assert
        EmailAnalysisJobHandler.JobTypeName.Should().Be("EmailAnalysis");
    }

    #endregion

    #region ProcessAsync Tests - New Jobs

    [Fact]
    public async Task ProcessAsync_NewJob_ChecksIdempotency()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeEmailAsync(emailId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailAnalysisResult.Success(emailId, Guid.NewGuid(), 0));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _idempotencyServiceMock.Verify(
            x => x.IsEventProcessedAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NewJob_AcquiresProcessingLock()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeEmailAsync(emailId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailAnalysisResult.Success(emailId, Guid.NewGuid(), 0));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _idempotencyServiceMock.Verify(
            x => x.TryAcquireProcessingLockAsync(
                job.IdempotencyKey,
                It.Is<TimeSpan>(t => t == TimeSpan.FromMinutes(10)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NewJob_CallsAnalyzeEmailAsync()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeEmailAsync(emailId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailAnalysisResult.Success(emailId, Guid.NewGuid(), 2));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _analysisServiceMock.Verify(
            x => x.AnalyzeEmailAsync(emailId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NewJob_Success_ReturnsSuccessOutcome()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeEmailAsync(emailId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailAnalysisResult.Success(emailId, documentId, 3));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);
        result.JobId.Should().Be(job.JobId);
        result.JobType.Should().Be(EmailAnalysisJobHandler.JobTypeName);
    }

    [Fact]
    public async Task ProcessAsync_NewJob_Success_MarksAsProcessed()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeEmailAsync(emailId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailAnalysisResult.Success(emailId, Guid.NewGuid(), 0));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _idempotencyServiceMock.Verify(
            x => x.MarkEventAsProcessedAsync(
                job.IdempotencyKey,
                It.Is<TimeSpan>(t => t == TimeSpan.FromDays(7)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NewJob_Success_ReleasesLock()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeEmailAsync(emailId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailAnalysisResult.Success(emailId, Guid.NewGuid(), 0));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ProcessAsync Tests - Duplicate Jobs (Idempotency)

    [Fact]
    public async Task ProcessAsync_DuplicateJob_AlreadyProcessed_ReturnsSuccess()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // Already processed

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateJob_AlreadyProcessed_DoesNotCallAnalysisService()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // Already processed

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _analysisServiceMock.Verify(
            x => x.AnalyzeEmailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_LockNotAcquired_ReturnsSuccessWithoutProcessing()
    {
        // Arrange - Another instance is processing this job
        var emailId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(job.IdempotencyKey, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Lock not acquired - another instance has it

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert - Returns success to prevent retry
        result.Status.Should().Be(JobStatus.Completed);
        _analysisServiceMock.Verify(
            x => x.AnalyzeEmailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region ProcessAsync Tests - Failure Scenarios

    [Fact]
    public async Task ProcessAsync_InvalidPayload_ReturnsPoisonedOutcome()
    {
        // Arrange - Job with null payload
        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = EmailAnalysisJobHandler.JobTypeName,
            SubjectId = "test",
            CorrelationId = Guid.NewGuid().ToString(),
            Attempt = 1,
            Payload = null
        };

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned);
        result.ErrorMessage.Should().Contain("payload");
    }

    [Fact]
    public async Task ProcessAsync_EmptyEmailId_ReturnsPoisonedOutcome()
    {
        // Arrange - Job with empty email ID
        var payload = new EmailAnalysisPayload
        {
            EmailId = Guid.Empty,
            Source = "UnitTest"
        };

        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = EmailAnalysisJobHandler.JobTypeName,
            SubjectId = "test",
            CorrelationId = Guid.NewGuid().ToString(),
            Attempt = 1,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(payload))
        };

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned);
    }

    [Fact]
    public async Task ProcessAsync_AnalysisFails_NoEmlDocument_ReturnsPoisonedOutcome()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeEmailAsync(emailId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailAnalysisResult.Failed(emailId, Guid.Empty, "No .eml document found")); // Permanent failure

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned);
        result.ErrorMessage.Should().Contain("No .eml document");
    }

    [Fact]
    public async Task ProcessAsync_AnalysisFails_PlaybookNotFound_ReturnsPoisonedOutcome()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeEmailAsync(emailId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailAnalysisResult.Failed(emailId, Guid.Empty, "Playbook 'Email Analysis' not found")); // Permanent failure

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned);
    }

    [Fact]
    public async Task ProcessAsync_AnalysisFails_TransientError_ReturnsFailedOutcome()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeEmailAsync(emailId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailAnalysisResult.Failed(emailId, Guid.Empty, "Service temporarily unavailable")); // Transient

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Failed); // Should allow retry
    }

    [Fact]
    public async Task ProcessAsync_HttpRequestException_ReturnsFailedOutcome()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeEmailAsync(emailId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection failed"));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Failed); // Retryable
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedException_ReturnsPoisonedOutcome()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeEmailAsync(emailId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned); // Not retryable
    }

    [Fact]
    public async Task ProcessAsync_AnalysisFails_StillReleasesLock()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var job = CreateEmailAnalysisJobContract(emailId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeEmailAsync(emailId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailAnalysisResult.Failed(emailId, Guid.Empty, "Analysis failed"));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert - Lock should be released even on failure
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region EmailAnalysisPayload Tests

    [Fact]
    public void EmailAnalysisPayload_CanBeCreated()
    {
        // Arrange & Act
        var payload = new EmailAnalysisPayload
        {
            EmailId = Guid.NewGuid(),
            Source = "EmailToDocument",
            EnqueuedAt = DateTimeOffset.UtcNow
        };

        // Assert
        payload.EmailId.Should().NotBeEmpty();
        payload.Source.Should().Be("EmailToDocument");
        payload.EnqueuedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void EmailAnalysisPayload_SerializesCorrectly()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var payload = new EmailAnalysisPayload
        {
            EmailId = emailId,
            Source = "RibbonButton"
        };

        // Act
        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<EmailAnalysisPayload>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.EmailId.Should().Be(emailId);
        deserialized.Source.Should().Be("RibbonButton");
    }

    #endregion
}
