using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Email;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Email;

/// <summary>
/// Unit tests for AttachmentFilterService.
/// Tests all filtering rules: blocked extensions, signature patterns, tracking pixels,
/// calendar files, inline attachments, and size thresholds.
/// </summary>
public class AttachmentFilterServiceTests
{
    private readonly Mock<ILogger<AttachmentFilterService>> _loggerMock;
    private readonly EmailProcessingOptions _defaultOptions;

    public AttachmentFilterServiceTests()
    {
        _loggerMock = new Mock<ILogger<AttachmentFilterService>>();
        _defaultOptions = new EmailProcessingOptions
        {
            MaxAttachmentSizeMB = 25,
            BlockedAttachmentExtensions = [".exe", ".dll", ".bat", ".ps1", ".vbs", ".js", ".cmd"],
            SignatureImagePatterns =
            [
                @"^image\d{3}\.(png|gif|jpg|jpeg)$",
                @"^spacer\.(gif|png)$",
                @"^logo.*\.(png|gif|jpg|jpeg)$",
                @"^signature.*\.(png|gif|jpg|jpeg)$"
            ],
            MinImageSizeKB = 5,
            FilterCalendarFiles = true,
            CalendarFileExtensions = [".ics", ".vcs"],
            FilterInlineAttachments = true,
            TrackingPixelPatterns =
            [
                @"\.gif\?.*tracking",
                @"pixel\.(gif|png)",
                @"^spacer\d*\.(gif|png)$",
                @"beacon\.(gif|png)",
                @"^1x1\.(gif|png)$",
                @"tracker\.(gif|png)",
                @"open\.(gif|png)"
            ]
        };
    }

    private AttachmentFilterService CreateService(EmailProcessingOptions? options = null)
    {
        var opts = Options.Create(options ?? _defaultOptions);
        return new AttachmentFilterService(opts, _loggerMock.Object);
    }

    #region FilterAttachments Tests

    [Fact]
    public void FilterAttachments_EmptyList_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var attachments = new List<EmailAttachmentInfo>();

        // Act
        var result = service.FilterAttachments(attachments);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void FilterAttachments_AllValid_ReturnsAll()
    {
        // Arrange
        var service = CreateService();
        var attachments = new List<EmailAttachmentInfo>
        {
            CreateAttachment("document.pdf", "application/pdf", 50000),
            CreateAttachment("report.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 75000),
            CreateAttachment("data.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 100000)
        };

        // Act
        var result = service.FilterAttachments(attachments);

        // Assert
        result.Should().HaveCount(3);
    }

    [Fact]
    public void FilterAttachments_NullInput_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        var act = () => service.FilterAttachments(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ShouldFilterAttachment - Blocked Extensions

    [Theory]
    [InlineData(".exe", "application/octet-stream")]
    [InlineData(".dll", "application/octet-stream")]
    [InlineData(".bat", "text/plain")]
    [InlineData(".ps1", "text/plain")]
    [InlineData(".vbs", "text/plain")]
    [InlineData(".js", "text/javascript")]
    [InlineData(".cmd", "text/plain")]
    public void Filter_BlockedExtension_Excluded(string extension, string mimeType)
    {
        // Arrange
        var service = CreateService();
        var attachment = CreateAttachment($"file{extension}", mimeType, 1000);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("Blocked extension");
        reason.Should().Contain(extension);
    }

    [Fact]
    public void Filter_AllowedExtension_NotFiltered()
    {
        // Arrange
        var service = CreateService();
        var attachment = CreateAttachment("document.pdf", "application/pdf", 50000);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeFalse();
        reason.Should().BeNull();
    }

    #endregion

    #region ShouldFilterAttachment - Size Limits

    [Fact]
    public void Filter_ExceedsMaxSize_Excluded()
    {
        // Arrange
        var options = new EmailProcessingOptions
        {
            MaxAttachmentSizeMB = 1 // 1MB limit
        };
        var service = CreateService(options);
        var attachment = CreateAttachment("large.pdf", "application/pdf", 2_000_000); // 2MB

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("max size");
    }

    [Fact]
    public void Filter_UnderMaxSize_NotFiltered()
    {
        // Arrange
        var service = CreateService();
        var attachment = CreateAttachment("normal.pdf", "application/pdf", 500_000); // 500KB

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeFalse();
    }

    [Fact]
    public void Filter_SmallImage_Excluded()
    {
        // Arrange - image under 5KB threshold
        var service = CreateService();
        var attachment = CreateAttachment("icon.png", "image/png", 2000); // 2KB

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("Small image");
    }

    [Fact]
    public void Filter_LargeImage_NotFiltered()
    {
        // Arrange - image over 5KB threshold
        var service = CreateService();
        var attachment = CreateAttachment("photo.jpg", "image/jpeg", 50000); // 50KB

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeFalse();
    }

    [Fact]
    public void Filter_SmallNonImage_NotFiltered()
    {
        // Arrange - small file but not an image
        var service = CreateService();
        var attachment = CreateAttachment("config.json", "application/json", 500); // 500 bytes

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeFalse(); // Size threshold only applies to images
    }

    #endregion

    #region ShouldFilterAttachment - Signature Image Patterns

    [Theory]
    [InlineData("image001.png")]
    [InlineData("image123.gif")]
    [InlineData("image999.jpg")]
    public void Filter_SignatureImagePattern_Excluded(string filename)
    {
        // Arrange
        var service = CreateService();
        var attachment = CreateAttachment(filename, "image/png", 10000);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("signature");
    }

    [Theory]
    [InlineData("spacer.gif")]
    [InlineData("spacer.png")]
    public void Filter_SpacerImage_Excluded(string filename)
    {
        // Arrange
        // Note: spacer files match tracking pixel pattern "^spacer\d*\.(gif|png)$" first,
        // which is checked before signature patterns in AttachmentFilterService
        var service = CreateService();
        var attachment = CreateAttachment(filename, "image/gif", 10000);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("tracking pixel");
    }

    [Theory]
    [InlineData("logo.png")]
    [InlineData("logo_company.jpg")]
    [InlineData("logoSmall.gif")]
    public void Filter_LogoImage_Excluded(string filename)
    {
        // Arrange
        var service = CreateService();
        var attachment = CreateAttachment(filename, "image/png", 10000);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("signature");
    }

    [Theory]
    [InlineData("signature.png")]
    [InlineData("signature_john.jpg")]
    public void Filter_SignatureNamedImage_Excluded(string filename)
    {
        // Arrange
        var service = CreateService();
        var attachment = CreateAttachment(filename, "image/png", 10000);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("signature");
    }

    [Fact]
    public void Filter_NormalImage_NotFiltered()
    {
        // Arrange
        var service = CreateService();
        var attachment = CreateAttachment("vacation_photo.jpg", "image/jpeg", 500000);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeFalse();
    }

    #endregion

    #region ShouldFilterAttachment - Tracking Pixel Patterns

    [Theory]
    [InlineData("pixel.gif")]
    [InlineData("pixel.png")]
    public void Filter_TrackingPixel_Excluded(string filename)
    {
        // Arrange
        var service = CreateService();
        var attachment = CreateAttachment(filename, "image/gif", 100);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("tracking pixel");
    }

    [Theory]
    [InlineData("beacon.gif")]
    [InlineData("beacon.png")]
    public void Filter_Beacon_Excluded(string filename)
    {
        // Arrange
        var service = CreateService();
        var attachment = CreateAttachment(filename, "image/gif", 100);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("tracking pixel");
    }

    [Theory]
    [InlineData("1x1.gif")]
    [InlineData("1x1.png")]
    public void Filter_1x1Pixel_Excluded(string filename)
    {
        // Arrange
        var service = CreateService();
        var attachment = CreateAttachment(filename, "image/gif", 100);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("tracking pixel");
    }

    [Theory]
    [InlineData("tracker.gif")]
    [InlineData("open.png")]
    public void Filter_TrackerImage_Excluded(string filename)
    {
        // Arrange
        var service = CreateService();
        var attachment = CreateAttachment(filename, "image/gif", 100);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("tracking pixel");
    }

    [Theory]
    [InlineData("spacer1.gif")]
    [InlineData("spacer123.png")]
    public void Filter_NumberedSpacer_Excluded(string filename)
    {
        // Arrange
        var service = CreateService();
        var attachment = CreateAttachment(filename, "image/gif", 100);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("tracking pixel");
    }

    #endregion

    #region ShouldFilterAttachment - Calendar Files

    [Theory]
    [InlineData(".ics")]
    [InlineData(".vcs")]
    public void Filter_CalendarFile_ExcludedIfConfigured(string extension)
    {
        // Arrange
        var options = new EmailProcessingOptions
        {
            FilterCalendarFiles = true,
            CalendarFileExtensions = [".ics", ".vcs"]
        };
        var service = CreateService(options);
        var attachment = CreateAttachment($"meeting{extension}", "text/calendar", 5000);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("Calendar file");
    }

    [Fact]
    public void Filter_CalendarFile_NotExcludedIfDisabled()
    {
        // Arrange
        var options = new EmailProcessingOptions
        {
            FilterCalendarFiles = false,
            CalendarFileExtensions = [".ics", ".vcs"]
        };
        var service = CreateService(options);
        var attachment = CreateAttachment("meeting.ics", "text/calendar", 5000);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeFalse();
    }

    #endregion

    #region ShouldFilterAttachment - Inline Attachments

    [Fact]
    public void Filter_InlineAttachment_ExcludedIfConfigured()
    {
        // Arrange
        var options = new EmailProcessingOptions
        {
            FilterInlineAttachments = true
        };
        var service = CreateService(options);
        var attachment = CreateAttachment("inline_image.png", "image/png", 50000, isInline: true);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("Inline attachment");
    }

    [Fact]
    public void Filter_InlineAttachment_NotExcludedIfDisabled()
    {
        // Arrange
        var options = new EmailProcessingOptions
        {
            FilterInlineAttachments = false,
            MinImageSizeKB = 1 // Set low to not trigger size filter
        };
        var service = CreateService(options);
        var attachment = CreateAttachment("inline_image.png", "image/png", 50000, isInline: true);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeFalse();
    }

    #endregion

    #region ShouldFilterAttachment - Pre-filtered

    [Fact]
    public void Filter_PreFiltered_ExcludedWithOriginalReason()
    {
        // Arrange
        var service = CreateService();
        var attachment = new EmailAttachmentInfo
        {
            FileName = "document.pdf",
            MimeType = "application/pdf",
            SizeBytes = 50000,
            ShouldCreateDocument = false,
            SkipReason = "Already processed"
        };

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Be("Already processed");
    }

    #endregion

    #region ShouldFilterAttachment - Empty Filename

    [Fact]
    public void Filter_EmptyFilename_Excluded()
    {
        // Arrange
        var service = CreateService();
        var attachment = CreateAttachment("", "application/pdf", 50000);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("Empty filename");
    }

    [Fact]
    public void Filter_WhitespaceFilename_Excluded()
    {
        // Arrange
        var service = CreateService();
        var attachment = CreateAttachment("   ", "application/pdf", 50000);

        // Act
        var (shouldFilter, reason) = service.ShouldFilterAttachment(attachment);

        // Assert
        shouldFilter.Should().BeTrue();
        reason.Should().Contain("Empty filename");
    }

    #endregion

    #region ShouldFilterAttachment - Null Attachment

    [Fact]
    public void ShouldFilterAttachment_NullAttachment_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        var act = () => service.ShouldFilterAttachment(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Integration Scenarios

    [Fact]
    public void Filter_TypicalEmailAttachments_CorrectlyFiltered()
    {
        // Arrange - typical email with mix of meaningful and noise attachments
        var service = CreateService();
        var attachments = new List<EmailAttachmentInfo>
        {
            CreateAttachment("Contract.pdf", "application/pdf", 150000), // Valid
            CreateAttachment("Spreadsheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 75000), // Valid
            CreateAttachment("image001.png", "image/png", 10000), // Signature pattern
            CreateAttachment("spacer.gif", "image/gif", 100), // Spacer
            CreateAttachment("pixel.gif", "image/gif", 50), // Tracking pixel
            CreateAttachment("meeting.ics", "text/calendar", 2000), // Calendar
            CreateAttachment("logo.png", "image/png", 5000), // Logo
            CreateAttachment("Photo.jpg", "image/jpeg", 2000000) // Valid large photo
        };

        // Act
        var result = service.FilterAttachments(attachments);

        // Assert
        result.Should().HaveCount(3);
        result.Select(a => a.FileName).Should().BeEquivalentTo(["Contract.pdf", "Spreadsheet.xlsx", "Photo.jpg"]);
    }

    [Fact]
    public void Filter_SecurityThreatAttachments_AllExcluded()
    {
        // Arrange - email with potentially malicious attachments
        var service = CreateService();
        var attachments = new List<EmailAttachmentInfo>
        {
            CreateAttachment("malware.exe", "application/octet-stream", 50000),
            CreateAttachment("trojan.dll", "application/octet-stream", 100000),
            CreateAttachment("script.bat", "text/plain", 500),
            CreateAttachment("payload.ps1", "text/plain", 2000),
            CreateAttachment("macro.vbs", "text/plain", 1500)
        };

        // Act
        var result = service.FilterAttachments(attachments);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region Helper Methods

    private static EmailAttachmentInfo CreateAttachment(
        string fileName,
        string mimeType,
        long sizeBytes,
        bool isInline = false,
        string? contentId = null)
    {
        return new EmailAttachmentInfo
        {
            AttachmentId = Guid.NewGuid(),
            FileName = fileName,
            MimeType = mimeType,
            SizeBytes = sizeBytes,
            IsInline = isInline,
            ContentId = contentId,
            ShouldCreateDocument = true,
            SkipReason = null
        };
    }

    #endregion
}
