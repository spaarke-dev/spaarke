using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for <see cref="DocumentParserRouter"/>.
///
/// Covers the four acceptance criteria from AIPL-012:
///   1. LlamaParse disabled → always routes to DocumentIntelligenceService
///   2. LlamaParse enabled, large doc (>30 pages) → routes to LlamaParse
///   3. LlamaParse throws → falls back to DocumentIntelligenceService, logs warning
///   4. Small doc with LlamaParse enabled → routes to DocumentIntelligenceService
/// </summary>
public class DocumentParserRouterTests
{
    // -------------------------------------------------------------------------
    // Test fixtures
    // -------------------------------------------------------------------------

    private readonly Mock<DocumentIntelligenceService> _docIntelMock;
    private readonly Mock<LlamaParseClient> _llamaClientMock;
    private readonly Mock<ILogger<DocumentParserRouter>> _loggerMock;

    private static readonly ParsedDocument DocIntelResult = new()
    {
        Text = "Extracted via DocumentIntelligence",
        Pages = 5,
        ParserUsed = DocumentParser.DocumentIntelligence,
        ExtractedAt = DateTimeOffset.UtcNow
    };

    private static readonly ParsedDocument LlamaResult = new()
    {
        Text = "Extracted via LlamaParse",
        Pages = 45,
        ParserUsed = DocumentParser.LlamaParse,
        ExtractedAt = DateTimeOffset.UtcNow
    };

    public DocumentParserRouterTests()
    {
        _docIntelMock = new Mock<DocumentIntelligenceService>(
            Mock.Of<ITextExtractor>(),
            Mock.Of<ILogger<DocumentIntelligenceService>>());

        _llamaClientMock = new Mock<LlamaParseClient>(
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<IOptions<LlamaParseOptions>>(o => o.Value == new LlamaParseOptions
            {
                Enabled = true,
                BaseUrl = "https://api.cloud.llamaindex.ai",
                ParseTimeoutSeconds = 120,
                ApiKeySecretName = "llamaparse-api-key"
            }),
            Mock.Of<IConfiguration>(),
            Mock.Of<ILogger<LlamaParseClient>>());

        _loggerMock = new Mock<ILogger<DocumentParserRouter>>();
    }

    private DocumentParserRouter CreateRouter(bool llamaEnabled)
    {
        var options = Options.Create(new LlamaParseOptions
        {
            Enabled = llamaEnabled,
            BaseUrl = "https://api.cloud.llamaindex.ai",
            ParseTimeoutSeconds = 120,
            ApiKeySecretName = "llamaparse-api-key"
        });

        return new DocumentParserRouter(
            _docIntelMock.Object,
            _llamaClientMock.Object,
            options,
            _loggerMock.Object);
    }

    // -------------------------------------------------------------------------
    // Test 1: LlamaParse disabled → always use DocumentIntelligenceService
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParseDocumentAsync_LlamaParseDisabled_AlwaysUsesDocumentIntelligence()
    {
        // Arrange
        _docIntelMock
            .Setup(s => s.ParseDocumentAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocIntelResult);

        var router = CreateRouter(llamaEnabled: false);
        var content = BuildSmallPdfBytes(); // Small document

        // Act
        var result = await router.ParseDocumentAsync(content, "contract.pdf", "application/pdf");

        // Assert
        result.Should().Be(DocIntelResult);
        result.ParserUsed.Should().Be(DocumentParser.DocumentIntelligence);

        _docIntelMock.Verify(s => s.ParseDocumentAsync(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _llamaClientMock.Verify(s => s.ParseDocumentAsync(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ParseDocumentAsync_LlamaParseDisabled_LargeDoc_StillUsesDocumentIntelligence()
    {
        // Arrange — even a large document should use DocIntel when LlamaParse is disabled
        _docIntelMock
            .Setup(s => s.ParseDocumentAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocIntelResult);

        var router = CreateRouter(llamaEnabled: false);
        var content = BuildLargePdfBytes(pageCount: 45); // Large document

        // Act
        var result = await router.ParseDocumentAsync(content, "large-contract.pdf", "application/pdf");

        // Assert
        result.ParserUsed.Should().Be(DocumentParser.DocumentIntelligence);

        _llamaClientMock.Verify(s => s.ParseDocumentAsync(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Test 2: LlamaParse enabled + large doc → routes to LlamaParse
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParseDocumentAsync_LlamaParseEnabled_LargeDoc_UsesLlamaParse()
    {
        // Arrange
        _llamaClientMock
            .Setup(s => s.ParseDocumentAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(LlamaResult);

        var router = CreateRouter(llamaEnabled: true);
        var content = BuildLargePdfBytes(pageCount: 45); // > 30 page threshold

        // Act
        var result = await router.ParseDocumentAsync(content, "large-contract.pdf", "application/pdf");

        // Assert
        result.Should().Be(LlamaResult);
        result.ParserUsed.Should().Be(DocumentParser.LlamaParse);

        _llamaClientMock.Verify(s => s.ParseDocumentAsync(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _docIntelMock.Verify(s => s.ParseDocumentAsync(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ParseDocumentAsync_LlamaParseEnabled_ScannedMimeType_UsesLlamaParse()
    {
        // Arrange — TIFF files indicate scanned documents
        _llamaClientMock
            .Setup(s => s.ParseDocumentAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(LlamaResult);

        var router = CreateRouter(llamaEnabled: true);
        var content = new byte[1024]; // small bytes, but MIME indicates scanned doc

        // Act
        var result = await router.ParseDocumentAsync(content, "scan.tif", "image/tiff");

        // Assert
        result.ParserUsed.Should().Be(DocumentParser.LlamaParse);

        _llamaClientMock.Verify(s => s.ParseDocumentAsync(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Test 3: LlamaParse throws → falls back silently to DocumentIntelligence
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParseDocumentAsync_LlamaParseThrows_FallsBackToDocIntel_LogsWarning()
    {
        // Arrange
        _llamaClientMock
            .Setup(s => s.ParseDocumentAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LlamaParse API is unavailable"));

        _docIntelMock
            .Setup(s => s.ParseDocumentAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocIntelResult);

        var router = CreateRouter(llamaEnabled: true);
        var content = BuildLargePdfBytes(pageCount: 45);

        // Act — should NOT throw even though LlamaParse throws
        var result = await router.ParseDocumentAsync(content, "large-contract.pdf", "application/pdf");

        // Assert — caller receives DocIntel result (fallback)
        result.Should().Be(DocIntelResult);
        result.ParserUsed.Should().Be(DocumentParser.DocumentIntelligence);

        // Verify fallback was used
        _docIntelMock.Verify(s => s.ParseDocumentAsync(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify warning was logged (non-zero invocations of the warning overload)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            "Expected a warning to be logged when LlamaParse fails");
    }

    [Fact]
    public async Task ParseDocumentAsync_LlamaParseThrowsTimeout_FallsBackToDocIntel()
    {
        // Arrange — Timeout is also a graceful fallback scenario
        _llamaClientMock
            .Setup(s => s.ParseDocumentAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("LlamaParse timed out after 120 seconds"));

        _docIntelMock
            .Setup(s => s.ParseDocumentAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocIntelResult);

        var router = CreateRouter(llamaEnabled: true);
        var content = BuildLargePdfBytes(pageCount: 50);

        // Act
        var result = await router.ParseDocumentAsync(content, "large-doc.pdf", "application/pdf");

        // Assert
        result.Should().Be(DocIntelResult);
    }

    // -------------------------------------------------------------------------
    // Test 4: Small document with LlamaParse enabled → routes to DocumentIntelligence
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParseDocumentAsync_LlamaParseEnabled_SmallDoc_UsesDocumentIntelligence()
    {
        // Arrange — 5 pages is below the 30-page threshold
        _docIntelMock
            .Setup(s => s.ParseDocumentAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocIntelResult);

        var router = CreateRouter(llamaEnabled: true);
        var content = BuildLargePdfBytes(pageCount: 5); // < 30 page threshold

        // Act
        var result = await router.ParseDocumentAsync(content, "small-nda.pdf", "application/pdf");

        // Assert
        result.Should().Be(DocIntelResult);
        result.ParserUsed.Should().Be(DocumentParser.DocumentIntelligence);

        _docIntelMock.Verify(s => s.ParseDocumentAsync(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _llamaClientMock.Verify(s => s.ParseDocumentAsync(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ParseDocumentAsync_LlamaParseEnabled_ExactlyThirtyPages_UsesDocumentIntelligence()
    {
        // Arrange — exactly 30 pages should NOT trigger LlamaParse (threshold is > 30)
        _docIntelMock
            .Setup(s => s.ParseDocumentAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocIntelResult);

        var router = CreateRouter(llamaEnabled: true);
        var content = BuildLargePdfBytes(pageCount: 30);

        // Act
        var result = await router.ParseDocumentAsync(content, "contract.pdf", "application/pdf");

        // Assert
        result.ParserUsed.Should().Be(DocumentParser.DocumentIntelligence);

        _llamaClientMock.Verify(s => s.ParseDocumentAsync(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Input validation tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParseDocumentAsync_NullContent_ThrowsArgumentNullException()
    {
        var router = CreateRouter(llamaEnabled: false);

        var act = () => router.ParseDocumentAsync(null!, "file.pdf", "application/pdf");

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("content");
    }

    [Fact]
    public async Task ParseDocumentAsync_EmptyFileName_ThrowsArgumentException()
    {
        var router = CreateRouter(llamaEnabled: false);

        var act = () => router.ParseDocumentAsync(new byte[100], string.Empty, "application/pdf");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("fileName");
    }

    // -------------------------------------------------------------------------
    // Constructor validation tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_NullDocIntelService_ThrowsArgumentNullException()
    {
        var options = Options.Create(new LlamaParseOptions());

        var act = () => new DocumentParserRouter(
            null!,
            _llamaClientMock.Object,
            options,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("docIntelService");
    }

    [Fact]
    public void Constructor_NullLlamaParseClient_ThrowsArgumentNullException()
    {
        var options = Options.Create(new LlamaParseOptions());

        var act = () => new DocumentParserRouter(
            _docIntelMock.Object,
            null!,
            options,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("llamaParseClient");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var act = () => new DocumentParserRouter(
            _docIntelMock.Object,
            _llamaClientMock.Object,
            null!,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var options = Options.Create(new LlamaParseOptions());

        var act = () => new DocumentParserRouter(
            _docIntelMock.Object,
            _llamaClientMock.Object,
            options,
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    // -------------------------------------------------------------------------
    // Helpers — build fake PDF content with /Type /Page markers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds minimal PDF-like bytes containing the specified number of
    /// <c>/Type /Page</c> markers. Used to exercise the page-count estimator
    /// in <see cref="DocumentParserRouter"/>.
    /// </summary>
    private static byte[] BuildLargePdfBytes(int pageCount)
    {
        const string pageMarker = "/Type /Page";
        var headerBytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n");
        var markerBytes = System.Text.Encoding.ASCII.GetBytes(pageMarker + "\n");

        var buffer = new List<byte>(headerBytes);
        for (var i = 0; i < pageCount; i++)
        {
            buffer.AddRange(markerBytes);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Builds minimal PDF-like bytes with no page markers (simulates a tiny document
    /// where the page count cannot be estimated from raw bytes).
    /// </summary>
    private static byte[] BuildSmallPdfBytes()
    {
        return System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n%% Empty document");
    }
}
