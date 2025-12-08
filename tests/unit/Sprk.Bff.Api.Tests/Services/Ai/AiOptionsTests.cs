using FluentAssertions;
using Sprk.Bff.Api.Configuration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public class AiOptionsTests
{
    [Fact]
    public void DefaultValues_ShouldBeSetCorrectly()
    {
        var options = new AiOptions();

        options.Enabled.Should().BeTrue();
        options.StreamingEnabled.Should().BeTrue();
        options.SummarizeModel.Should().Be("gpt-4o-mini");
        options.MaxOutputTokens.Should().Be(1000);
        options.Temperature.Should().Be(0.3f);
        options.MaxFileSizeBytes.Should().Be(10 * 1024 * 1024);
        options.MaxInputTokens.Should().Be(100_000);
        options.MaxConcurrentStreams.Should().Be(3);
    }

    [Fact]
    public void DefaultSupportedFileTypes_ShouldIncludeExpectedTypes()
    {
        var options = new AiOptions();

        options.SupportedFileTypes.Should().ContainKey(".txt");
        options.SupportedFileTypes.Should().ContainKey(".md");
        options.SupportedFileTypes.Should().ContainKey(".json");
        options.SupportedFileTypes.Should().ContainKey(".csv");
        options.SupportedFileTypes.Should().ContainKey(".pdf");
        options.SupportedFileTypes.Should().ContainKey(".docx");
        options.SupportedFileTypes.Should().ContainKey(".png");
    }

    [Theory]
    [InlineData(".txt", true)]
    [InlineData(".md", true)]
    [InlineData(".json", true)]
    [InlineData(".csv", true)]
    [InlineData(".pdf", true)]
    [InlineData(".docx", true)]
    [InlineData(".doc", true)]
    [InlineData(".png", true)] // Now enabled for vision
    [InlineData(".jpg", true)] // Now enabled for vision
    [InlineData(".unknown", false)]
    public void IsFileTypeSupported_ReturnsCorrectResult(string extension, bool expected)
    {
        var options = new AiOptions();

        var result = options.IsFileTypeSupported(extension);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("txt", true)] // Without dot
    [InlineData(".TXT", true)] // Uppercase
    [InlineData(".Txt", true)] // Mixed case
    public void IsFileTypeSupported_HandlesVariousFormats(string extension, bool expected)
    {
        var options = new AiOptions();

        var result = options.IsFileTypeSupported(extension);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(".txt", ExtractionMethod.Native)]
    [InlineData(".md", ExtractionMethod.Native)]
    [InlineData(".json", ExtractionMethod.Native)]
    [InlineData(".pdf", ExtractionMethod.DocumentIntelligence)]
    [InlineData(".docx", ExtractionMethod.DocumentIntelligence)]
    public void GetExtractionMethod_ReturnsCorrectMethod(string extension, ExtractionMethod expected)
    {
        var options = new AiOptions();

        var result = options.GetExtractionMethod(extension);

        result.Should().Be(expected);
    }

    [Fact]
    public void GetExtractionMethod_ReturnsNull_ForUnknownExtension()
    {
        var options = new AiOptions();

        var result = options.GetExtractionMethod(".unknown");

        result.Should().BeNull();
    }

    [Fact]
    public void GetExtractionMethod_ReturnsVisionOcr_ForImageExtension()
    {
        var options = new AiOptions();

        // PNG is now enabled by default for vision
        var result = options.GetExtractionMethod(".png");

        result.Should().Be(ExtractionMethod.VisionOcr);
    }

    [Fact]
    public void GetExtractionMethod_ReturnsNull_WhenExtensionDisabled()
    {
        var options = new AiOptions();
        // Disable PNG
        options.SupportedFileTypes[".png"] = new FileTypeConfig
        {
            Enabled = false,
            Method = ExtractionMethod.VisionOcr
        };

        var result = options.GetExtractionMethod(".png");

        result.Should().BeNull();
    }

    [Fact]
    public void SupportedFileTypes_CanBeModified()
    {
        var options = new AiOptions();

        // Disable PDF
        options.SupportedFileTypes[".pdf"].Enabled = false;

        options.IsFileTypeSupported(".pdf").Should().BeFalse();
        options.GetExtractionMethod(".pdf").Should().BeNull();
    }

    [Fact]
    public void SupportedFileTypes_CanAddNewExtension()
    {
        var options = new AiOptions();

        options.SupportedFileTypes[".rtf"] = new FileTypeConfig
        {
            Enabled = true,
            Method = ExtractionMethod.DocumentIntelligence
        };

        options.IsFileTypeSupported(".rtf").Should().BeTrue();
        options.GetExtractionMethod(".rtf").Should().Be(ExtractionMethod.DocumentIntelligence);
    }

    [Fact]
    public void NativeTextExtensions_ShouldHaveNativeMethod()
    {
        var options = new AiOptions();
        var nativeExtensions = new[] { ".txt", ".md", ".json", ".csv", ".xml", ".html" };

        foreach (var ext in nativeExtensions)
        {
            var config = options.SupportedFileTypes[ext];
            config.Enabled.Should().BeTrue($"{ext} should be enabled by default");
            config.Method.Should().Be(ExtractionMethod.Native, $"{ext} should use Native extraction");
        }
    }

    [Fact]
    public void DocumentIntelligenceExtensions_ShouldHaveDocIntelMethod()
    {
        var options = new AiOptions();
        var docIntelExtensions = new[] { ".pdf", ".docx", ".doc" };

        foreach (var ext in docIntelExtensions)
        {
            var config = options.SupportedFileTypes[ext];
            config.Enabled.Should().BeTrue($"{ext} should be enabled by default");
            config.Method.Should().Be(ExtractionMethod.DocumentIntelligence,
                $"{ext} should use DocumentIntelligence extraction");
        }
    }

    [Fact]
    public void ImageExtensions_ShouldBeEnabledWithVisionOcrMethod()
    {
        var options = new AiOptions();
        var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".tiff", ".bmp", ".webp" };

        foreach (var ext in imageExtensions)
        {
            var config = options.SupportedFileTypes[ext];
            config.Enabled.Should().BeTrue($"{ext} should be enabled for vision processing");
            config.Method.Should().Be(ExtractionMethod.VisionOcr, $"{ext} should use VisionOcr extraction");
        }
    }

    [Fact]
    public void VisionPromptTemplate_ShouldHaveDefaultValue()
    {
        var options = new AiOptions();

        options.VisionPromptTemplate.Should().NotBeNullOrEmpty();
        options.VisionPromptTemplate.Should().Contain("image");
    }
}

public class FileTypeConfigTests
{
    [Fact]
    public void FileTypeConfig_DefaultValues()
    {
        var config = new FileTypeConfig();

        config.Enabled.Should().BeFalse();
        config.Method.Should().Be(ExtractionMethod.Native);
    }

    [Fact]
    public void FileTypeConfig_CanBeConfigured()
    {
        var config = new FileTypeConfig
        {
            Enabled = true,
            Method = ExtractionMethod.DocumentIntelligence
        };

        config.Enabled.Should().BeTrue();
        config.Method.Should().Be(ExtractionMethod.DocumentIntelligence);
    }
}

public class ExtractionMethodTests
{
    [Fact]
    public void ExtractionMethod_HasExpectedValues()
    {
        Enum.GetValues<ExtractionMethod>().Should().HaveCount(3);
        Enum.IsDefined(ExtractionMethod.Native).Should().BeTrue();
        Enum.IsDefined(ExtractionMethod.DocumentIntelligence).Should().BeTrue();
        Enum.IsDefined(ExtractionMethod.VisionOcr).Should().BeTrue();
    }
}
