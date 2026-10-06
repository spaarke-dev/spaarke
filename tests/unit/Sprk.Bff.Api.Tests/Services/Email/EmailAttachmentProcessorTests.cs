using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Email;
using Xunit;
using Sprk.Bff.Api.Tests.TestInfrastructure;

namespace Sprk.Bff.Api.Tests.Services.Email;

/// <summary>
/// Tests for <see cref="EmailAttachmentProcessor.ShouldFilterAttachment"/> — the real production
/// filtering logic (ADR-038: a test must exercise the real thing).
/// </summary>
/// <remarks>
/// Task 095 (spaarke-ontology-platform-r1, 2026-10-04) replaced a hand-copied
/// <c>AttachmentFilterTestHelper</c> class that DUPLICATED <see cref="EmailAttachmentProcessor"/>'s
/// filtering logic (including its own, divergent copy of the signature-regex construction) rather
/// than calling it. That duplicate was the root cause of
/// <c>ShouldFilterAttachment_LogoPattern_ReturnsTrue</c>'s reported flake under a contended full BFF
/// run: both the duplicate and the real method built each signature <see cref="System.Text.RegularExpressions.Regex"/>
/// with a hardcoded <c>TimeSpan.FromSeconds(1)</c> match timeout, but unlike the real method the
/// duplicate had no <c>try/catch</c> around <c>IsMatch</c> — so under genuine thread-scheduling
/// starvation (not algorithmic slowness; the patterns here are simple and non-backtracking) a
/// timeout would have surfaced as an unhandled <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/>
/// rather than the graceful fail-open the production code already implements.
///
/// Mechanism proof: <see cref="ShouldFilterAttachment_RegexTimesOut_FailsOpenAndDoesNotFilter"/>
/// forces the per-pattern timeout down to <see cref="TimeSpan.FromTicks"/>(1) — a budget so small
/// that the regex engine's first elapsed-time check (which happens after entering the match, not
/// continuously) is guaranteed to have already exceeded it, reproducing the exact same internal
/// condition a multi-second real scheduling delay would produce, deterministically rather than by
/// chance. It pins the production contract (fails open, never throws) that the old duplicate
/// helper lacked.
///
/// Fix for the flake itself: <see cref="EmailProcessingOptions.SignatureImageRegexTimeout"/> makes
/// the per-pattern timeout configurable (production default unchanged at 1 second). The tests in
/// this file construct the real processor with a materially more generous timeout
/// (<see cref="GenerousTestTimeout"/>) because a unit-test process competing with dozens of other
/// parallel xunit collections for CPU is a different execution environment than a single
/// production request thread, and 1 second is not calibrated for that environment. This is NOT a
/// change to production's default — see the constructor option below and
/// notes/095-mechanism-evidence.md for the load-based verification.
/// </remarks>
public class EmailAttachmentProcessorTests
{
    /// <summary>
    /// Generous per-pattern regex timeout for THESE tests only. Chosen to comfortably absorb the
    /// thread-scheduling jitter of a heavily parallel test run without masking a genuine defect —
    /// these patterns match in well under a millisecond on any real CPU, so there is no scenario
    /// where legitimate matching work needs anywhere near this budget. Production's own default
    /// (<see cref="EmailProcessingOptions.SignatureImageRegexTimeout"/>) remains 1 second.
    /// </summary>
    private static readonly TimeSpan GenerousTestTimeout = TimeSpan.FromSeconds(5);

    private readonly EmailAttachmentProcessor _processor;

    public EmailAttachmentProcessorTests()
    {
        _processor = CreateProcessor(new EmailProcessingOptions
        {
            SignatureImagePatterns =
            [
                @"^image\d{3}\.(png|gif|jpg|jpeg)$",
                @"^spacer\.(gif|png)$",
                @"^logo.*\.(png|gif|jpg|jpeg)$",
                @"^signature.*\.(png|gif|jpg|jpeg)$"
            ],
            MinImageSizeKB = 5,
            SignatureImageRegexTimeout = GenerousTestTimeout
        });
    }

    /// <summary>
    /// Constructs a REAL <see cref="EmailAttachmentProcessor"/> for unit testing its pure filtering
    /// logic. <see cref="ShouldFilterAttachment"/> never touches <c>speFileStore</c> or
    /// <c>documentService</c>, so both are satisfied with the minimum needed to construct: a real
    /// <see cref="SpeFileStore"/> built from mocked Graph primitives (the codebase idiom — see
    /// <c>OfficeStorageUploaderDeleteTests.BuildSpeMock</c> — no transport-shaped mocking per
    /// ADR-038 B1) and a trivially mocked <see cref="IDocumentDataverseService"/>.
    /// </summary>
    private static EmailAttachmentProcessor CreateProcessor(EmailProcessingOptions options)
    {
        var graphClientFactory = Mock.Of<IGraphClientFactory>();
        var speFileStore = new SpeFileStore(
            new ContainerOperations(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), NullLogger<ContainerOperations>.Instance),
            new DriveItemOperations(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), NullLogger<DriveItemOperations>.Instance),
            new UploadSessionManager(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<IHttpClientFactory>(), NullLogger<UploadSessionManager>.Instance),
            new UserOperations(graphClientFactory, NullLogger<UserOperations>.Instance));

        return new EmailAttachmentProcessor(
            speFileStore,
            Mock.Of<IDocumentDataverseService>(),
            Options.Create(options),
            NullLogger<EmailAttachmentProcessor>.Instance);
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
        var result = _processor.ShouldFilterAttachment(fileName, sizeBytes, "application/octet-stream");

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
        var result = _processor.ShouldFilterAttachment(fileName, sizeBytes, "application/octet-stream");

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
        var result = _processor.ShouldFilterAttachment(fileName, sizeBytes, "image/png");

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
        var result = _processor.ShouldFilterAttachment(fileName, sizeBytes, "image/gif");

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
        var result = _processor.ShouldFilterAttachment(fileName, sizeBytes, "image/png");

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
        var result = _processor.ShouldFilterAttachment(fileName, sizeBytes, "image/png");

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
        var result = _processor.ShouldFilterAttachment(fileName, sizeBytes, "image/png");

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
        var result = _processor.ShouldFilterAttachment(fileName, sizeBytes, "image/png");

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
        var result = _processor.ShouldFilterAttachment(fileName, sizeBytes, "image/png");

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
        var result = _processor.ShouldFilterAttachment(fileName, sizeBytes, contentType);

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
        var result = _processor.ShouldFilterAttachment(fileName, sizeBytes, null);

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
        var result = _processor.ShouldFilterAttachment(fileName, sizeBytes, "application/pdf");

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
        var result = _processor.ShouldFilterAttachment(fileName!, 10000, "application/pdf");

        // Assert
        result.Should().BeTrue("empty or null filenames should be filtered");
    }

    [Fact]
    public void ShouldFilterAttachment_CaseInsensitiveExtension_ReturnsTrue()
    {
        // Arrange - uppercase extension
        var fileName = "SCRIPT.EXE";

        // Act
        var result = _processor.ShouldFilterAttachment(fileName, 10000, "application/octet-stream");

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
        var result = _processor.ShouldFilterAttachment(fileName, sizeBytes, "image/png");

        // Assert
        result.Should().BeTrue("pattern matching should be case-insensitive");
    }

    #endregion

    #region Regex Timeout Mechanism (task 095)

    /// <summary>
    /// Mechanism proof + permanent regression coverage for the fail-open contract. Forces the
    /// per-pattern timeout down to <c>TimeSpan.FromTicks(1)</c> (100 nanoseconds) — a budget so far
    /// below the time needed to even query the clock and compare that the FIRST timeout check the
    /// regex engine performs is guaranteed to already be over budget, regardless of host CPU speed.
    /// This reproduces, deterministically, the exact internal condition ("elapsed time since match
    /// start exceeds the configured budget") that a real multi-second thread-scheduling delay under
    /// heavy CPU contention would also produce — proving the hypothesized mechanism rather than
    /// assuming it.
    /// </summary>
    [Fact]
    public void ShouldFilterAttachment_RegexTimesOut_FailsOpenAndDoesNotFilter()
    {
        // Arrange - forced near-zero timeout stands in for a real scheduling-induced overrun.
        var processor = CreateProcessor(new EmailProcessingOptions
        {
            SignatureImagePatterns = [@"^logo.*\.(png|gif|jpg|jpeg)$"],
            MinImageSizeKB = 5,
            SignatureImageRegexTimeout = TimeSpan.FromTicks(1)
        });

        // Act - "logo.png" would match the signature pattern under any normal budget; forcing the
        // timeout this low makes the match throw RegexMatchTimeoutException before it can
        // complete. Production's IsSignatureImage catches it and continues to the size check.
        var act = () => processor.ShouldFilterAttachment("logo.png", sizeBytes: 10240, contentType: "image/png");

        // Assert - never throws (fails open), and since 10KB is also above the small-image
        // threshold, the overall result is `false`: the attachment is NOT filtered. This is the
        // documented, deliberate degradation (log a warning, keep the attachment) — not a crash,
        // and not a silently-wrong "filtered" verdict either.
        var result = act.Should().NotThrow(
            "EmailAttachmentProcessor.IsSignatureImage must catch RegexMatchTimeoutException and fail open, never throw").Subject;
        result.Should().BeFalse(
            "a timed-out signature check must fail open (treated as non-match), and 10KB is above the small-image threshold");
    }

    #endregion
}
