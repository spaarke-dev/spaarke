using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public class TextExtractorServiceTests
{
    private readonly Mock<ILogger<TextExtractorService>> _loggerMock;
    private readonly IOptions<AiOptions> _options;
    private readonly TextExtractorService _service;

    public TextExtractorServiceTests()
    {
        _loggerMock = new Mock<ILogger<TextExtractorService>>();
        _options = Options.Create(new AiOptions());
        _service = new TextExtractorService(_options, _loggerMock.Object);
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".json")]
    [InlineData(".csv")]
    [InlineData(".xml")]
    [InlineData(".html")]
    public async Task ExtractAsync_NativeTextFile_ReturnsSuccess(string extension)
    {
        var content = "Hello, World! This is test content.";
        using var stream = CreateStream(content);

        var result = await _service.ExtractAsync(stream, $"test{extension}");

        result.Success.Should().BeTrue();
        result.Text.Should().Be(content);
        result.Method.Should().Be(TextExtractionMethod.Native);
        result.CharacterCount.Should().Be(content.Length);
    }

    [Fact]
    public async Task ExtractAsync_TxtFile_ReturnsNativeMethod()
    {
        var content = "Simple text file content.";
        using var stream = CreateStream(content);

        var result = await _service.ExtractAsync(stream, "document.txt");

        result.Success.Should().BeTrue();
        result.Text.Should().Be(content);
        result.Method.Should().Be(TextExtractionMethod.Native);
    }

    [Fact]
    public async Task ExtractAsync_MarkdownFile_ReturnsContent()
    {
        var content = "# Heading\n\nThis is **markdown** content.";
        using var stream = CreateStream(content);

        var result = await _service.ExtractAsync(stream, "README.md");

        result.Success.Should().BeTrue();
        result.Text.Should().Be(content);
    }

    [Fact]
    public async Task ExtractAsync_JsonFile_ReturnsContent()
    {
        var content = "{\"name\": \"test\", \"value\": 123}";
        using var stream = CreateStream(content);

        var result = await _service.ExtractAsync(stream, "config.json");

        result.Success.Should().BeTrue();
        result.Text.Should().Be(content);
    }

    [Fact]
    public async Task ExtractAsync_CsvFile_ReturnsContent()
    {
        var content = "name,value\ntest,123\nfoo,456";
        using var stream = CreateStream(content);

        var result = await _service.ExtractAsync(stream, "data.csv");

        result.Success.Should().BeTrue();
        result.Text.Should().Be(content);
    }

    [Fact]
    public async Task ExtractAsync_PdfFile_WhenDocIntelNotConfigured_ReturnsConfigurationError()
    {
        // Default options have no DocIntel configured
        using var stream = CreateStream("fake pdf content");

        var result = await _service.ExtractAsync(stream, "document.pdf");

        result.Success.Should().BeFalse();
        result.Method.Should().Be(TextExtractionMethod.DocumentIntelligence);
        result.ErrorMessage.Should().Contain("not configured");
    }

    [Fact]
    public async Task ExtractAsync_DocxFile_WhenDocIntelNotConfigured_ReturnsConfigurationError()
    {
        // Default options have no DocIntel configured
        using var stream = CreateStream("fake docx content");

        var result = await _service.ExtractAsync(stream, "document.docx");

        result.Success.Should().BeFalse();
        result.Method.Should().Be(TextExtractionMethod.DocumentIntelligence);
        result.ErrorMessage.Should().Contain("not configured");
    }

    [Fact]
    public async Task ExtractAsync_DocFile_WhenDocIntelNotConfigured_ReturnsConfigurationError()
    {
        // Default options have no DocIntel configured
        using var stream = CreateStream("fake doc content");

        var result = await _service.ExtractAsync(stream, "document.doc");

        result.Success.Should().BeFalse();
        result.Method.Should().Be(TextExtractionMethod.DocumentIntelligence);
        result.ErrorMessage.Should().Contain("not configured");
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    [InlineData(".doc")]
    public async Task ExtractAsync_DocIntelTypes_WhenEndpointMissing_ReturnsConfigurationError(string extension)
    {
        // Only key configured, endpoint missing
        var options = Options.Create(new AiOptions
        {
            DocIntelKey = "test-key"
            // DocIntelEndpoint not set
        });
        var service = new TextExtractorService(options, _loggerMock.Object);

        using var stream = CreateStream("content");
        var result = await service.ExtractAsync(stream, $"document{extension}");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured");
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    [InlineData(".doc")]
    public async Task ExtractAsync_DocIntelTypes_WhenKeyMissing_ReturnsConfigurationError(string extension)
    {
        // Only endpoint configured, key missing
        var options = Options.Create(new AiOptions
        {
            DocIntelEndpoint = "https://test.cognitiveservices.azure.com/"
            // DocIntelKey not set
        });
        var service = new TextExtractorService(options, _loggerMock.Object);

        using var stream = CreateStream("content");
        var result = await service.ExtractAsync(stream, $"document{extension}");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured");
    }

    [Fact]
    public async Task ExtractAsync_DocIntelFile_WhenFileTooLarge_ReturnsFileSizeError()
    {
        // Configure DocIntel but with small max file size
        var options = Options.Create(new AiOptions
        {
            DocIntelEndpoint = "https://test.cognitiveservices.azure.com/",
            DocIntelKey = "test-key",
            MaxFileSizeBytes = 100 // 100 bytes max
        });
        var service = new TextExtractorService(options, _loggerMock.Object);

        var content = new string('a', 200); // 200 bytes > 100 bytes limit
        using var stream = CreateStream(content);

        var result = await service.ExtractAsync(stream, "document.pdf");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exceeds maximum");
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".gif")]
    [InlineData(".bmp")]
    [InlineData(".tiff")]
    [InlineData(".webp")]
    public async Task ExtractAsync_ImageFile_WhenVisionNotConfigured_ReturnsConfigurationError(string extension)
    {
        // Default options have no ImageSummarizeModel configured
        using var stream = CreateStream("fake image");

        var result = await _service.ExtractAsync(stream, $"image{extension}");

        result.Success.Should().BeFalse();
        result.Method.Should().Be(TextExtractionMethod.VisionOcr);
        result.ErrorMessage.Should().Contain("not configured");
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".gif")]
    public async Task ExtractAsync_ImageFile_WhenVisionConfigured_ReturnsRequiresVision(string extension)
    {
        // Configure vision model
        var options = Options.Create(new AiOptions
        {
            ImageSummarizeModel = "gpt-4o"
        });
        var service = new TextExtractorService(options, _loggerMock.Object);

        using var stream = CreateStream("fake image");

        var result = await service.ExtractAsync(stream, $"image{extension}");

        result.Success.Should().BeTrue();
        result.Method.Should().Be(TextExtractionMethod.VisionOcr);
        result.Text.Should().BeNull(); // No text extracted, vision model will process directly
        result.IsVisionRequired.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_UnknownExtension_ReturnsNotSupported()
    {
        using var stream = CreateStream("content");

        var result = await _service.ExtractAsync(stream, "file.xyz");

        result.Success.Should().BeFalse();
        result.Method.Should().Be(TextExtractionMethod.NotSupported);
        result.ErrorMessage.Should().Contain(".xyz");
    }

    [Fact]
    public async Task ExtractAsync_EmptyFile_ReturnsFailed()
    {
        using var stream = CreateStream("");

        var result = await _service.ExtractAsync(stream, "empty.txt");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task ExtractAsync_WhitespaceOnlyFile_ReturnsFailed()
    {
        using var stream = CreateStream("   \n\t  ");

        var result = await _service.ExtractAsync(stream, "whitespace.txt");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task ExtractAsync_Utf8WithBom_ReturnsCorrectContent()
    {
        var content = "UTF-8 content with BOM";
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var fullBytes = bom.Concat(contentBytes).ToArray();
        using var stream = new MemoryStream(fullBytes);

        var result = await _service.ExtractAsync(stream, "utf8bom.txt");

        result.Success.Should().BeTrue();
        result.Text.Should().Be(content);
    }

    [Fact]
    public async Task ExtractAsync_Utf16LittleEndian_ReturnsCorrectContent()
    {
        var content = "UTF-16 LE content";
        var encoding = Encoding.Unicode; // UTF-16 LE
        var preamble = encoding.GetPreamble();
        var contentBytes = encoding.GetBytes(content);
        var fullBytes = preamble.Concat(contentBytes).ToArray();
        using var stream = new MemoryStream(fullBytes);

        var result = await _service.ExtractAsync(stream, "utf16le.txt");

        result.Success.Should().BeTrue();
        result.Text.Should().Be(content);
    }

    [Fact]
    public async Task ExtractAsync_Utf16BigEndian_ReturnsCorrectContent()
    {
        var content = "UTF-16 BE content";
        var encoding = Encoding.BigEndianUnicode; // UTF-16 BE
        var preamble = encoding.GetPreamble();
        var contentBytes = encoding.GetBytes(content);
        var fullBytes = preamble.Concat(contentBytes).ToArray();
        using var stream = new MemoryStream(fullBytes);

        var result = await _service.ExtractAsync(stream, "utf16be.txt");

        result.Success.Should().BeTrue();
        result.Text.Should().Be(content);
    }

    [Theory]
    [InlineData(".TXT")]
    [InlineData(".Txt")]
    [InlineData(".MD")]
    [InlineData(".Json")]
    public async Task ExtractAsync_CaseInsensitiveExtension_Works(string extension)
    {
        var content = "Content with different case extension.";
        using var stream = CreateStream(content);

        var result = await _service.ExtractAsync(stream, $"file{extension}");

        result.Success.Should().BeTrue();
        result.Text.Should().Be(content);
    }

    [Fact]
    public async Task ExtractAsync_LargeFile_TruncatesContent()
    {
        var options = Options.Create(new AiOptions
        {
            MaxInputTokens = 100 // Very small limit for testing
        });
        var service = new TextExtractorService(options, _loggerMock.Object);

        // Create content larger than limit (100 tokens * 4 chars = 400 chars)
        var content = new string('a', 1000);
        using var stream = CreateStream(content);

        var result = await service.ExtractAsync(stream, "large.txt");

        result.Success.Should().BeTrue();
        result.Text.Should().Contain("[Content truncated");
        result.CharacterCount.Should().BeLessThan(1000);
    }

    [Fact]
    public async Task ExtractAsync_FileExceedsMaxSize_ReturnsFailed()
    {
        var options = Options.Create(new AiOptions
        {
            MaxFileSizeBytes = 100 // 100 bytes max
        });
        var service = new TextExtractorService(options, _loggerMock.Object);

        var content = new string('a', 200); // 200 chars > 100 bytes
        using var stream = CreateStream(content);

        var result = await service.ExtractAsync(stream, "toolarge.txt");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exceeds maximum");
    }

    [Fact]
    public void IsSupported_EnabledType_ReturnsTrue()
    {
        var result = _service.IsSupported(".txt");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsSupported_ImageType_ReturnsTrue()
    {
        // Image types are now enabled by default
        var result = _service.IsSupported(".png");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsSupported_UnknownType_ReturnsFalse()
    {
        var result = _service.IsSupported(".xyz");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsSupported_WithoutDot_Works()
    {
        var result = _service.IsSupported("txt");

        result.Should().BeTrue();
    }

    [Fact]
    public void GetMethod_NativeType_ReturnsNative()
    {
        var result = _service.GetMethod(".txt");

        result.Should().Be(ExtractionMethod.Native);
    }

    [Fact]
    public void GetMethod_DocIntelType_ReturnsDocIntel()
    {
        var result = _service.GetMethod(".pdf");

        result.Should().Be(ExtractionMethod.DocumentIntelligence);
    }

    [Fact]
    public void GetMethod_ImageType_ReturnsVisionOcr()
    {
        // Image types are now enabled by default
        var result = _service.GetMethod(".png");

        result.Should().Be(ExtractionMethod.VisionOcr);
    }

    [Fact]
    public void GetMethod_UnknownType_ReturnsNull()
    {
        var result = _service.GetMethod(".xyz");

        result.Should().BeNull();
    }

    private static MemoryStream CreateStream(string content)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(content));
    }
}

public class TextExtractionResultTests
{
    [Fact]
    public void Succeeded_CreatesSuccessfulResult()
    {
        var result = TextExtractionResult.Succeeded("content", TextExtractionMethod.Native);

        result.Success.Should().BeTrue();
        result.Text.Should().Be("content");
        result.Method.Should().Be(TextExtractionMethod.Native);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Failed_CreatesFailedResult()
    {
        var result = TextExtractionResult.Failed("error", TextExtractionMethod.Native);

        result.Success.Should().BeFalse();
        result.Text.Should().BeNull();
        result.ErrorMessage.Should().Be("error");
    }

    [Fact]
    public void NotSupported_CreatesNotSupportedResult()
    {
        var result = TextExtractionResult.NotSupported(".xyz");

        result.Success.Should().BeFalse();
        result.Method.Should().Be(TextExtractionMethod.NotSupported);
        result.ErrorMessage.Should().Contain(".xyz");
    }

    [Fact]
    public void Disabled_CreatesDisabledResult()
    {
        var result = TextExtractionResult.Disabled(".png");

        result.Success.Should().BeFalse();
        result.Method.Should().Be(TextExtractionMethod.Disabled);
        result.ErrorMessage.Should().Contain(".png");
    }

    [Fact]
    public void CharacterCount_ReturnsCorrectCount()
    {
        var result = TextExtractionResult.Succeeded("12345", TextExtractionMethod.Native);

        result.CharacterCount.Should().Be(5);
    }

    [Fact]
    public void CharacterCount_ReturnsZero_WhenTextIsNull()
    {
        var result = TextExtractionResult.Failed("error", TextExtractionMethod.Native);

        result.CharacterCount.Should().Be(0);
    }

    [Fact]
    public void EstimatedTokenCount_ReturnsApproximation()
    {
        // 100 chars / 4 = 25 tokens
        var result = TextExtractionResult.Succeeded(new string('a', 100), TextExtractionMethod.Native);

        result.EstimatedTokenCount.Should().Be(25);
    }

    [Fact]
    public void RequiresVision_CreatesVisionRequiredResult()
    {
        var result = TextExtractionResult.RequiresVision();

        result.Success.Should().BeTrue();
        result.Text.Should().BeNull();
        result.Method.Should().Be(TextExtractionMethod.VisionOcr);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void IsVisionRequired_ReturnsTrue_WhenVisionMethodWithNullText()
    {
        var result = TextExtractionResult.RequiresVision();

        result.IsVisionRequired.Should().BeTrue();
    }

    [Fact]
    public void IsVisionRequired_ReturnsFalse_WhenNativeMethod()
    {
        var result = TextExtractionResult.Succeeded("content", TextExtractionMethod.Native);

        result.IsVisionRequired.Should().BeFalse();
    }

    [Fact]
    public void IsVisionRequired_ReturnsFalse_WhenVisionMethodFailed()
    {
        var result = TextExtractionResult.Failed("not configured", TextExtractionMethod.VisionOcr);

        result.IsVisionRequired.Should().BeFalse();
    }
}
