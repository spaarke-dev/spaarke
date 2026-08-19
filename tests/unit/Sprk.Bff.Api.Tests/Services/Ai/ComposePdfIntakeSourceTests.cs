using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Task 073 (spaarkeai-compose-r7, FR-11 / LOW-10) — unit tests for the discriminated facade result
/// <see cref="ComposePdfIntakeSource"/> now produces on intake failure. Before this task, every failure
/// collapsed into one generic logged message + a null return; these tests assert that circuit-open,
/// timeout, and corrupt-file causes are each classified distinctly with a cause-specific message
/// (<see cref="ComposePdfIntakeSource.ParseWithDiagnosticsAsync"/>), while <see
/// cref="ComposePdfIntakeSource.ParseAsync"/> (the <see cref="IComposePdfIntakeSource"/> member) keeps
/// its pre-existing never-throws / null-on-failure contract unchanged for existing callers.
///
/// The mocked <see cref="DocumentIntelligenceService"/> throws the SAME
/// <see cref="InvalidOperationException"/> shape production code produces
/// (<c>DocumentIntelligenceService.ParseDocumentLayoutAsync</c> wraps
/// <c>TextExtractorService.ExtractLayoutAsync</c>'s distinct Failed() wordings verbatim) — these tests
/// do not fork or reimplement Services/Ai internals, they exercise the real
/// <see cref="DocumentParserRouter"/> with a mocked <see cref="DocumentIntelligenceService"/> collaborator,
/// mirroring the existing <c>DocumentParserRouterTests</c> construction pattern.
/// </summary>
public class ComposePdfIntakeSourceTests
{
    private readonly Mock<DocumentIntelligenceService> _docIntelMock;
    private readonly Mock<LlamaParseClient> _llamaClientMock;
    private readonly Mock<ILogger<ComposePdfIntakeSource>> _sourceLoggerMock;

    private static readonly DocumentLayout SampleLayout = new()
    {
        PageCount = 3,
        Blocks = new List<DocumentLayoutBlock>
        {
            new() { Paragraph = new DocumentLayoutParagraph("Body text", DocumentLayoutParagraphRole.Body, 1) },
        },
    };

    public ComposePdfIntakeSourceTests()
    {
        _docIntelMock = new Mock<DocumentIntelligenceService>(
            Mock.Of<ITextExtractor>(),
            Mock.Of<ILogger<DocumentIntelligenceService>>());

        _llamaClientMock = new Mock<LlamaParseClient>(
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<IOptions<LlamaParseOptions>>(o => o.Value == new LlamaParseOptions
            {
                Enabled = false,
                BaseUrl = "https://api.cloud.llamaindex.ai",
                ParseTimeoutSeconds = 120,
                ApiKeySecretName = "llamaparse-api-key",
            }),
            Mock.Of<IConfiguration>(),
            Mock.Of<ILogger<LlamaParseClient>>());

        _sourceLoggerMock = new Mock<ILogger<ComposePdfIntakeSource>>();
    }

    private ComposePdfIntakeSource CreateSource()
    {
        var options = Options.Create(new LlamaParseOptions { Enabled = false });
        var router = new DocumentParserRouter(
            _docIntelMock.Object,
            _llamaClientMock.Object,
            options,
            Mock.Of<ILogger<DocumentParserRouter>>());

        return new ComposePdfIntakeSource(router, _sourceLoggerMock.Object);
    }

    private void SetupDocIntelThrows(string fileName, string exceptionMessage)
    {
        _docIntelMock
            .Setup(s => s.ParseDocumentLayoutAsync(
                It.IsAny<byte[]>(),
                fileName,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(exceptionMessage));
    }

    // -------------------------------------------------------------------------
    // Success path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParseWithDiagnosticsAsync_WhenExtractionSucceeds_ReturnsSuccessResult()
    {
        _docIntelMock
            .Setup(s => s.ParseDocumentLayoutAsync(
                It.IsAny<byte[]>(),
                "contract.pdf",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleLayout);

        var sut = CreateSource();

        var result = await sut.ParseWithDiagnosticsAsync(new byte[] { 1, 2, 3 }, "contract.pdf", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Layout.Should().Be(SampleLayout);
        result.FailureCause.Should().BeNull();
        result.FailureMessage.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_WhenExtractionSucceeds_ReturnsLayout()
    {
        _docIntelMock
            .Setup(s => s.ParseDocumentLayoutAsync(
                It.IsAny<byte[]>(),
                "contract.pdf",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleLayout);

        var sut = CreateSource();

        var layout = await sut.ParseAsync(new byte[] { 1, 2, 3 }, "contract.pdf", CancellationToken.None);

        layout.Should().Be(SampleLayout);
    }

    // -------------------------------------------------------------------------
    // Cause discrimination — the acceptance criterion this task adds
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParseWithDiagnosticsAsync_WhenCircuitBreakerOpen_ReturnsCircuitOpenCauseWithSpecificMessage()
    {
        SetupDocIntelThrows(
            "contract.pdf",
            "Azure Document Intelligence failed to extract layout from 'contract.pdf': " +
            "Document layout extraction is temporarily unavailable due to repeated service failures. " +
            "Please try again in a few minutes.");

        var sut = CreateSource();

        var result = await sut.ParseWithDiagnosticsAsync(new byte[] { 1 }, "contract.pdf", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureCause.Should().Be(PdfIntakeFailureCause.CircuitOpen);
        result.FailureMessage.Should().Contain("circuit breaker is open");
        result.FailureMessage.Should().Contain("contract.pdf");
    }

    [Fact]
    public async Task ParseWithDiagnosticsAsync_WhenExtractionTimesOut_ReturnsTimeoutCauseWithSpecificMessage()
    {
        SetupDocIntelThrows(
            "large-nda.pdf",
            "Azure Document Intelligence failed to extract layout from 'large-nda.pdf': " +
            "Document layout extraction took too long (exceeded 60s timeout). Please try again later.");

        var sut = CreateSource();

        var result = await sut.ParseWithDiagnosticsAsync(new byte[] { 1 }, "large-nda.pdf", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureCause.Should().Be(PdfIntakeFailureCause.Timeout);
        result.FailureMessage.Should().Contain("timed out");
        result.FailureMessage.Should().Contain("large-nda.pdf");
    }

    [Fact]
    public async Task ParseWithDiagnosticsAsync_WhenDocumentIsCorrupt_ReturnsCorruptCauseWithSpecificMessage()
    {
        SetupDocIntelThrows(
            "damaged.pdf",
            "Azure Document Intelligence failed to extract layout from 'damaged.pdf': " +
            "The document format is invalid or unsupported by Document Intelligence.");

        var sut = CreateSource();

        var result = await sut.ParseWithDiagnosticsAsync(new byte[] { 1 }, "damaged.pdf", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureCause.Should().Be(PdfIntakeFailureCause.Corrupt);
        result.FailureMessage.Should().Contain("corrupt or in an unsupported format");
        result.FailureMessage.Should().Contain("damaged.pdf");
    }

    [Fact]
    public async Task ParseWithDiagnosticsAsync_WhenFailureCauseIsUnrecognized_ReturnsUnknownCauseWithCollapsedMessage()
    {
        SetupDocIntelThrows(
            "weird.pdf",
            "Azure Document Intelligence failed to extract layout from 'weird.pdf': " +
            "An unexpected internal error occurred.");

        var sut = CreateSource();

        var result = await sut.ParseWithDiagnosticsAsync(new byte[] { 1 }, "weird.pdf", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureCause.Should().Be(PdfIntakeFailureCause.Unknown);
        result.FailureMessage.Should().Contain("may be corrupt or the document-parsing service is unavailable");
    }

    [Theory]
    [InlineData(PdfIntakeFailureCause.CircuitOpen)]
    [InlineData(PdfIntakeFailureCause.Timeout)]
    [InlineData(PdfIntakeFailureCause.Corrupt)]
    [InlineData(PdfIntakeFailureCause.Unknown)]
    public async Task ParseWithDiagnosticsAsync_DistinctCauses_ProduceDistinctMessages(PdfIntakeFailureCause expectedCause)
    {
        var exceptionMessagesByCause = new Dictionary<PdfIntakeFailureCause, string>
        {
            [PdfIntakeFailureCause.CircuitOpen] =
                "Document layout extraction is temporarily unavailable due to repeated service failures.",
            [PdfIntakeFailureCause.Timeout] =
                "Document layout extraction took too long (exceeded 60s timeout).",
            [PdfIntakeFailureCause.Corrupt] =
                "The document format is invalid or unsupported by Document Intelligence.",
            [PdfIntakeFailureCause.Unknown] =
                "Some unexpected failure occurred.",
        };

        SetupDocIntelThrows("multi.pdf", exceptionMessagesByCause[expectedCause]);

        var sut = CreateSource();

        var result = await sut.ParseWithDiagnosticsAsync(new byte[] { 1 }, "multi.pdf", CancellationToken.None);

        result.FailureCause.Should().Be(expectedCause);
    }

    [Fact]
    public async Task ParseWithDiagnosticsAsync_AllFourCauses_ProduceFourDistinctMessages()
    {
        var causesAndSourceMessages = new (PdfIntakeFailureCause Cause, string SourceMessage)[]
        {
            (PdfIntakeFailureCause.CircuitOpen, "temporarily unavailable due to repeated service failures"),
            (PdfIntakeFailureCause.Timeout, "took too long"),
            (PdfIntakeFailureCause.Corrupt, "invalid or unsupported"),
            (PdfIntakeFailureCause.Unknown, "some other never-before-seen error text"),
        };

        var messages = new List<string>();
        foreach (var (_, sourceMessage) in causesAndSourceMessages)
        {
            SetupDocIntelThrows("doc.pdf", sourceMessage);
            var sut = CreateSource();
            var result = await sut.ParseWithDiagnosticsAsync(new byte[] { 1 }, "doc.pdf", CancellationToken.None);
            messages.Add(result.FailureMessage!);
        }

        messages.Should().OnlyHaveUniqueItems();
    }

    // -------------------------------------------------------------------------
    // ParseAsync back-compat: still collapses to null regardless of cause (unchanged contract)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParseAsync_WhenExtractionFailsForAnyCause_StillReturnsNull()
    {
        SetupDocIntelThrows("contract.pdf", "temporarily unavailable due to repeated service failures");

        var sut = CreateSource();

        var layout = await sut.ParseAsync(new byte[] { 1 }, "contract.pdf", CancellationToken.None);

        layout.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Cancellation still propagates (unchanged contract)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParseWithDiagnosticsAsync_WhenCallerCancels_PropagatesOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();

        _docIntelMock
            .Setup(s => s.ParseDocumentLayoutAsync(
                It.IsAny<byte[]>(),
                "contract.pdf",
                It.IsAny<CancellationToken>()))
            .Returns<byte[], string, CancellationToken>((_, _, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(SampleLayout);
            });

        var sut = CreateSource();

        var act = () => sut.ParseWithDiagnosticsAsync(new byte[] { 1 }, "contract.pdf", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -------------------------------------------------------------------------
    // Input validation (unchanged contract)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParseWithDiagnosticsAsync_NullBytes_ThrowsArgumentNullException()
    {
        var sut = CreateSource();

        var act = () => sut.ParseWithDiagnosticsAsync(null!, "contract.pdf", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("pdfBytes");
    }

    [Fact]
    public async Task ParseWithDiagnosticsAsync_EmptyFileName_ThrowsArgumentException()
    {
        var sut = CreateSource();

        var act = () => sut.ParseWithDiagnosticsAsync(new byte[] { 1 }, string.Empty, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("fileName");
    }

    // -------------------------------------------------------------------------
    // Constructor validation (unchanged contract)
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_NullParserRouter_ThrowsArgumentNullException()
    {
        var act = () => new ComposePdfIntakeSource(null!, _sourceLoggerMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("parserRouter");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var router = new DocumentParserRouter(
            _docIntelMock.Object,
            _llamaClientMock.Object,
            Options.Create(new LlamaParseOptions { Enabled = false }),
            Mock.Of<ILogger<DocumentParserRouter>>());

        var act = () => new ComposePdfIntakeSource(router, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}
