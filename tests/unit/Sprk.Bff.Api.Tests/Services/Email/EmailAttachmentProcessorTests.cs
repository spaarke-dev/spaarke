using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Email;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Email;

/// <summary>
/// Tests for EmailAttachmentProcessor filtering logic.
/// Uses a test helper to avoid complex dependency mocking for SpeFileStore.
/// </summary>
public class EmailAttachmentProcessorTests
{
    private readonly AttachmentFilterTestHelper _filterHelper;

    public EmailAttachmentProcessorTests()
    {
        var options = new EmailProcessingOptions
        {
            SignatureImagePatterns =
            [
                @"^image\d{3}\.(png|gif|jpg|jpeg)$",
                @"^spacer\.(gif|png)$",
                @"^logo.*\.(png|gif|jpg|jpeg)$",
                @"^signature.*\.(png|gif|jpg|jpeg)$"
            ],
            MinImageSizeKB = 5
        };

        _filterHelper = new AttachmentFilterTestHelper(options);
    }

    /// <summary>
    /// Test helper that exposes the attachment filtering logic without full dependencies.
    /// Mirrors the filtering logic from EmailAttachmentProcessor.
    /// </summary>
    private class AttachmentFilterTestHelper
    {
        private readonly EmailProcessingOptions _options;
        private readonly Regex[] _signaturePatterns;

        private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jse",
            ".wsf", ".wsh", ".msc", ".scr", ".pif", ".com", ".hta"
        };

        private static readonly HashSet<string> ImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/png", "image/gif", "image/jpeg", "image/jpg", "image/bmp", "image/webp"
        };

        public AttachmentFilterTestHelper(EmailProcessingOptions options)
        {
            _options = options;
            _signaturePatterns = options.SignatureImagePatterns
                .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)))
                .ToArray();
        }

        public bool ShouldFilterAttachment(string fileName, long sizeBytes, string? contentType)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return true;

            var extension = Path.GetExtension(fileName);
            if (BlockedExtensions.Contains(extension))
                return true;

            if (IsSignatureImage(fileName))
                return true;

            if (IsSmallImage(fileName, sizeBytes, contentType))
                return true;

            return false;
        }

        private bool IsSignatureImage(string fileName)
        {
            return _signaturePatterns.Any(p => p.IsMatch(fileName));
        }

        private bool IsSmallImage(string fileName, long sizeBytes, string? contentType)
        {
            var isImage = false;

            if (!string.IsNullOrEmpty(contentType) && ImageMimeTypes.Contains(contentType))
            {
                isImage = true;
            }
            else
            {
                var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
                isImage = extension is ".png" or ".gif" or ".jpg" or ".jpeg" or ".bmp" or ".webp";
            }

            if (!isImage)
                return false;

            var minSizeBytes = _options.MinImageSizeKB * 1024;
            return sizeBytes < minSizeBytes;
        }
    }

    #region Blocked Extension Tests

    [Theory]
    [InlineData(".exe", "malware.exe")]
    [InlineData(".dll", "library.dll")]
    [InlineData(".bat", "script.bat")]
    [InlineData(".cmd", "command.cmd")]
    [InlineData(".ps1", "powershell.ps1")]
    [InlineData(".vbs", "vbscript.vbs")]
    [InlineData(".js", "javascript.js")]
    [InlineData(".hta", "html_app.hta")]
    public void ShouldFilterAttachment_BlockedExtension_ReturnsTrue(string extension, string fileName)
    {
        // Arrange
        var sizeBytes = 10000L; // 10KB - above minimum threshold

        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName, sizeBytes, "application/octet-stream");

        // Assert
        result.Should().BeTrue($"extension {extension} should be blocked");
    }

    [Theory]
    [InlineData("document.pdf")]
    [InlineData("spreadsheet.xlsx")]
    [InlineData("report.docx")]
    [InlineData("presentation.pptx")]
    [InlineData("archive.zip")]
    [InlineData("image.png")]
    [InlineData("photo.jpg")]
    public void ShouldFilterAttachment_AllowedExtension_ReturnsFalse(string fileName)
    {
        // Arrange
        var sizeBytes = 100000L; // 100KB - above all thresholds

        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName, sizeBytes, "application/octet-stream");

        // Assert
        result.Should().BeFalse($"extension for {fileName} should be allowed");
    }

    #endregion

    #region Signature Image Pattern Tests

    [Theory]
    [InlineData("image001.png")]
    [InlineData("image002.gif")]
    [InlineData("image123.jpg")]
    [InlineData("image999.jpeg")]
    public void ShouldFilterAttachment_ImageNumberPattern_ReturnsTrue(string fileName)
    {
        // Arrange - 10KB, above min threshold
        var sizeBytes = 10240L;

        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName, sizeBytes, "image/png");

        // Assert
        result.Should().BeTrue($"'{fileName}' matches signature image pattern");
    }

    [Theory]
    [InlineData("spacer.gif")]
    [InlineData("spacer.png")]
    public void ShouldFilterAttachment_SpacerPattern_ReturnsTrue(string fileName)
    {
        // Arrange
        var sizeBytes = 10240L;

        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName, sizeBytes, "image/gif");

        // Assert
        result.Should().BeTrue($"'{fileName}' matches spacer pattern");
    }

    [Theory]
    [InlineData("logo.png")]
    [InlineData("logo_company.gif")]
    [InlineData("logoSmall.jpg")]
    public void ShouldFilterAttachment_LogoPattern_ReturnsTrue(string fileName)
    {
        // Arrange
        var sizeBytes = 10240L;

        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName, sizeBytes, "image/png");

        // Assert
        result.Should().BeTrue($"'{fileName}' matches logo pattern");
    }

    [Theory]
    [InlineData("signature.png")]
    [InlineData("signature_john.gif")]
    [InlineData("signatureBlock.jpg")]
    public void ShouldFilterAttachment_SignaturePattern_ReturnsTrue(string fileName)
    {
        // Arrange
        var sizeBytes = 10240L;

        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName, sizeBytes, "image/png");

        // Assert
        result.Should().BeTrue($"'{fileName}' matches signature pattern");
    }

    [Theory]
    [InlineData("chart.png")]
    [InlineData("screenshot.jpg")]
    [InlineData("diagram.gif")]
    [InlineData("photo_2024.jpeg")]
    public void ShouldFilterAttachment_NonSignatureImage_ReturnsFalse(string fileName)
    {
        // Arrange - 100KB, well above threshold
        var sizeBytes = 102400L;

        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName, sizeBytes, "image/png");

        // Assert
        result.Should().BeFalse($"'{fileName}' is not a signature image pattern");
    }

    #endregion

    #region Small Image Filtering Tests

    [Theory]
    [InlineData(1024, "1KB image")]      // 1KB
    [InlineData(2048, "2KB image")]      // 2KB
    [InlineData(4096, "4KB image")]      // 4KB
    [InlineData(5119, "just under 5KB")] // Just under 5KB threshold
    public void ShouldFilterAttachment_SmallImage_ReturnsTrue(long sizeBytes, string description)
    {
        // Arrange
        var fileName = "chart.png"; // Not a signature pattern

        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName, sizeBytes, "image/png");

        // Assert
        result.Should().BeTrue($"{description} should be filtered (< 5KB threshold)");
    }

    [Theory]
    [InlineData(5120, "exactly 5KB")]   // Exactly 5KB threshold
    [InlineData(10240, "10KB image")]   // 10KB
    [InlineData(102400, "100KB image")] // 100KB
    public void ShouldFilterAttachment_LargeEnoughImage_ReturnsFalse(long sizeBytes, string description)
    {
        // Arrange
        var fileName = "chart.png"; // Not a signature pattern

        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName, sizeBytes, "image/png");

        // Assert
        result.Should().BeFalse($"{description} should not be filtered (>= 5KB threshold)");
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/gif")]
    [InlineData("image/jpeg")]
    [InlineData("image/jpg")]
    [InlineData("image/bmp")]
    [InlineData("image/webp")]
    public void ShouldFilterAttachment_SmallImageByContentType_ReturnsTrue(string contentType)
    {
        // Arrange - 1KB, small image
        var fileName = "chart.dat"; // Extension doesn't indicate image
        var sizeBytes = 1024L;

        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName, sizeBytes, contentType);

        // Assert
        result.Should().BeTrue($"small file with {contentType} should be filtered");
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".gif")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".bmp")]
    [InlineData(".webp")]
    public void ShouldFilterAttachment_SmallImageByExtension_ReturnsTrue(string extension)
    {
        // Arrange - 1KB, small image
        var fileName = $"chart{extension}";
        var sizeBytes = 1024L;

        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName, sizeBytes, null);

        // Assert
        result.Should().BeTrue($"small file with {extension} extension should be filtered");
    }

    [Fact]
    public void ShouldFilterAttachment_SmallNonImage_ReturnsFalse()
    {
        // Arrange - 1KB PDF (not an image)
        var fileName = "tiny.pdf";
        var sizeBytes = 1024L;

        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName, sizeBytes, "application/pdf");

        // Assert
        result.Should().BeFalse("small non-image files should not be filtered by size");
    }

    #endregion

    #region Edge Cases

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldFilterAttachment_EmptyFileName_ReturnsTrue(string? fileName)
    {
        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName!, 10000, "application/pdf");

        // Assert
        result.Should().BeTrue("empty or null filenames should be filtered");
    }

    [Fact]
    public void ShouldFilterAttachment_CaseInsensitiveExtension_ReturnsTrue()
    {
        // Arrange - uppercase extension
        var fileName = "SCRIPT.EXE";

        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName, 10000, "application/octet-stream");

        // Assert
        result.Should().BeTrue("extension matching should be case-insensitive");
    }

    [Fact]
    public void ShouldFilterAttachment_CaseInsensitivePattern_ReturnsTrue()
    {
        // Arrange - uppercase pattern match
        var fileName = "IMAGE001.PNG";
        var sizeBytes = 10240L;

        // Act
        var result = _filterHelper.ShouldFilterAttachment(fileName, sizeBytes, "image/png");

        // Assert
        result.Should().BeTrue("pattern matching should be case-insensitive");
    }

    #endregion
}
