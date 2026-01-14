using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Email;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Email;

/// <summary>
/// Unit tests for EmailToEmlConverter.ExtractAttachments method.
/// Tests extraction of attachments from .eml files using MimeKit.
/// </summary>
public class EmailAttachmentExtractionTests
{
    private readonly Mock<ILogger<EmailToEmlConverter>> _loggerMock;
    private readonly IOptions<EmailProcessingOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _fakeCredential;

    public EmailAttachmentExtractionTests()
    {
        _loggerMock = new Mock<ILogger<EmailToEmlConverter>>();
        _options = Options.Create(new EmailProcessingOptions
        {
            MaxAttachmentSizeMB = 25,
            MaxTotalSizeMB = 100,
            BlockedAttachmentExtensions = [".exe", ".dll", ".bat"],
            SignatureImagePatterns = [@"^image\d{3}\.(png|gif|jpg)$", @"^spacer\.gif$"],
            MinImageSizeKB = 5,
            MaxEmlFileNameLength = 100
        });

        var configData = new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com"
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://test.crm.dynamics.com/api/data/v9.2/")
        };

        _fakeCredential = new FakeTokenCredential();
    }

    private EmailToEmlConverter CreateConverter(EmailProcessingOptions? optionsOverride = null)
    {
        var options = optionsOverride != null
            ? Options.Create(optionsOverride)
            : _options;

        return new EmailToEmlConverter(_httpClient, options, _configuration, _loggerMock.Object, _fakeCredential);
    }

    #region ExtractAttachments Tests

    [Fact]
    public void ExtractAttachments_EmailWithPdfAttachment_ReturnsPdf()
    {
        // Arrange
        var pdfContent = Encoding.UTF8.GetBytes("PDF document content");
        var emlStream = CreateEmlWithAttachments(new[]
        {
            ("document.pdf", "application/pdf", pdfContent)
        });

        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert
        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("document.pdf");
        result[0].MimeType.Should().Be("application/pdf");
        result[0].SizeBytes.Should().Be(pdfContent.Length);
        result[0].ShouldCreateDocument.Should().BeTrue();
        result[0].IsInline.Should().BeFalse();
    }

    [Fact]
    public void ExtractAttachments_EmailNoAttachments_ReturnsEmpty()
    {
        // Arrange
        var emlStream = CreateSimpleEml("Test Subject", "Plain text body without attachments");
        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractAttachments_EmailWithMultipleAttachments_ReturnsAll()
    {
        // Arrange
        var emlStream = CreateEmlWithAttachments(new[]
        {
            ("document.pdf", "application/pdf", Encoding.UTF8.GetBytes("PDF content")),
            ("report.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Encoding.UTF8.GetBytes("Word content")),
            ("data.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Encoding.UTF8.GetBytes("Excel content"))
        });

        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert
        result.Should().HaveCount(3);
        result.Select(a => a.FileName).Should().BeEquivalentTo(["document.pdf", "report.docx", "data.xlsx"]);
    }

    [Fact]
    public void ExtractAttachments_InlineImages_FlaggedCorrectly()
    {
        // Arrange
        var imageContent = new byte[10000]; // 10KB image
        var emlStream = CreateEmlWithInlineImage("logo.png", "image/png", imageContent, "logo123");

        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert
        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("logo.png");
        result[0].IsInline.Should().BeTrue();
        result[0].ContentId.Should().Be("logo123");
    }

    [Fact]
    public void ExtractAttachments_BlockedExtension_MarkedAsSkipped()
    {
        // Arrange
        var emlStream = CreateEmlWithAttachments(new[]
        {
            ("malware.exe", "application/octet-stream", Encoding.UTF8.GetBytes("bad content"))
        });

        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert
        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("malware.exe");
        result[0].ShouldCreateDocument.Should().BeFalse();
        result[0].SkipReason.Should().Contain(".exe");
    }

    [Fact]
    public void ExtractAttachments_SignatureImage_MarkedAsSkipped()
    {
        // Arrange
        var imageContent = new byte[100]; // Small image
        var emlStream = CreateEmlWithAttachments(new[]
        {
            ("image001.png", "image/png", imageContent)
        });

        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert
        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("image001.png");
        result[0].ShouldCreateDocument.Should().BeFalse();
        result[0].SkipReason.Should().Contain("signature");
    }

    [Fact]
    public void ExtractAttachments_SmallImage_MarkedAsSkipped()
    {
        // Arrange - image under 5KB threshold
        var imageContent = new byte[2000]; // 2KB
        var emlStream = CreateEmlWithAttachments(new[]
        {
            ("chart.png", "image/png", imageContent)
        });

        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert
        result.Should().HaveCount(1);
        result[0].ShouldCreateDocument.Should().BeFalse();
        result[0].SkipReason.Should().Contain("small");
    }

    [Fact]
    public void ExtractAttachments_LargeImage_IncludedForProcessing()
    {
        // Arrange - image over 5KB threshold
        var imageContent = new byte[10000]; // 10KB
        var emlStream = CreateEmlWithAttachments(new[]
        {
            ("photo.jpg", "image/jpeg", imageContent)
        });

        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert
        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("photo.jpg");
        result[0].ShouldCreateDocument.Should().BeTrue();
    }

    [Fact]
    public void ExtractAttachments_MixedAttachments_CorrectlyFiltered()
    {
        // Arrange - mix of valid and filtered attachments
        var emlStream = CreateEmlWithAttachments(new[]
        {
            ("report.pdf", "application/pdf", Encoding.UTF8.GetBytes("valid pdf")),
            ("virus.exe", "application/octet-stream", Encoding.UTF8.GetBytes("blocked")),
            ("image001.png", "image/png", new byte[100]), // signature pattern
            ("photo.jpg", "image/jpeg", new byte[10000]) // valid large image
        });

        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert
        result.Should().HaveCount(4);

        var shouldCreate = result.Where(a => a.ShouldCreateDocument).ToList();
        shouldCreate.Should().HaveCount(2);
        shouldCreate.Select(a => a.FileName).Should().Contain("report.pdf");
        shouldCreate.Select(a => a.FileName).Should().Contain("photo.jpg");

        var shouldSkip = result.Where(a => !a.ShouldCreateDocument).ToList();
        shouldSkip.Should().HaveCount(2);
        shouldSkip.Select(a => a.FileName).Should().Contain("virus.exe");
        shouldSkip.Select(a => a.FileName).Should().Contain("image001.png");
    }

    [Fact]
    public void ExtractAttachments_AttachmentWithoutFilename_GeneratesFilename()
    {
        // Arrange
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Test", "test@test.com"));
        message.To.Add(new MailboxAddress("Recipient", "recipient@test.com"));
        message.Subject = "Test";

        var builder = new BodyBuilder { TextBody = "Test body" };

        // Create attachment without filename
        var attachment = new MimePart("application", "pdf")
        {
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes("PDF content"))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment)
            // Intentionally not setting FileName
        };
        builder.Attachments.Add(attachment);

        message.Body = builder.ToMessageBody();

        var emlStream = new MemoryStream();
        message.WriteTo(emlStream);
        emlStream.Position = 0;

        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert
        result.Should().HaveCount(1);
        result[0].FileName.Should().StartWith("attachment_");
        result[0].FileName.Should().EndWith(".pdf");
    }

    [Fact]
    public void ExtractAttachments_NullStream_ThrowsArgumentNullException()
    {
        // Arrange
        var converter = CreateConverter();

        // Act & Assert
        var act = () => converter.ExtractAttachments(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExtractAttachments_StreamResetsPosition_ReadsFromStart()
    {
        // Arrange
        var emlStream = CreateEmlWithAttachments(new[]
        {
            ("document.pdf", "application/pdf", Encoding.UTF8.GetBytes("PDF content"))
        });

        // Move stream to end to simulate previous read
        emlStream.Position = emlStream.Length;

        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert - should still find attachment because stream is reset
        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("document.pdf");
    }

    [Fact]
    public void ExtractAttachments_ContentStreamIsReadable_ReturnsValidContent()
    {
        // Arrange
        var expectedContent = "Test PDF content for verification";
        var emlStream = CreateEmlWithAttachments(new[]
        {
            ("test.pdf", "application/pdf", Encoding.UTF8.GetBytes(expectedContent))
        });

        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert
        result.Should().HaveCount(1);
        result[0].Content.Should().NotBeNull();

        using var reader = new StreamReader(result[0].Content!);
        var content = reader.ReadToEnd();
        content.Should().Be(expectedContent);
    }

    [Fact]
    public void ExtractAttachments_SpacerGif_MarkedAsSkipped()
    {
        // Arrange - spacer.gif matches signature pattern
        var emlStream = CreateEmlWithAttachments(new[]
        {
            ("spacer.gif", "image/gif", new byte[100])
        });

        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert
        result.Should().HaveCount(1);
        result[0].ShouldCreateDocument.Should().BeFalse();
        result[0].SkipReason.Should().Contain("signature");
    }

    [Fact]
    public void ExtractAttachments_DllAttachment_MarkedAsBlocked()
    {
        // Arrange
        var emlStream = CreateEmlWithAttachments(new[]
        {
            ("library.dll", "application/octet-stream", Encoding.UTF8.GetBytes("binary content"))
        });

        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert
        result.Should().HaveCount(1);
        result[0].ShouldCreateDocument.Should().BeFalse();
        result[0].SkipReason.Should().Contain(".dll");
    }

    [Fact]
    public void ExtractAttachments_BatScript_MarkedAsBlocked()
    {
        // Arrange
        var emlStream = CreateEmlWithAttachments(new[]
        {
            ("setup.bat", "text/plain", Encoding.UTF8.GetBytes("echo hello"))
        });

        var converter = CreateConverter();

        // Act
        var result = converter.ExtractAttachments(emlStream);

        // Assert
        result.Should().HaveCount(1);
        result[0].ShouldCreateDocument.Should().BeFalse();
        result[0].SkipReason.Should().Contain(".bat");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Create a simple .eml stream without attachments.
    /// </summary>
    private static MemoryStream CreateSimpleEml(string subject, string body)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Test Sender", "sender@test.com"));
        message.To.Add(new MailboxAddress("Test Recipient", "recipient@test.com"));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        var stream = new MemoryStream();
        message.WriteTo(stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Create an .eml stream with attachments.
    /// </summary>
    private static MemoryStream CreateEmlWithAttachments(
        (string fileName, string mimeType, byte[] content)[] attachments)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Test Sender", "sender@test.com"));
        message.To.Add(new MailboxAddress("Test Recipient", "recipient@test.com"));
        message.Subject = "Test Email with Attachments";

        var builder = new BodyBuilder { TextBody = "This email has attachments." };

        foreach (var (fileName, mimeType, content) in attachments)
        {
            var parts = mimeType.Split('/');
            var mediaType = parts[0];
            var mediaSubtype = parts.Length > 1 ? parts[1] : "octet-stream";

            builder.Attachments.Add(fileName, content, new MimeKit.ContentType(mediaType, mediaSubtype));
        }

        message.Body = builder.ToMessageBody();

        var stream = new MemoryStream();
        message.WriteTo(stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Create an .eml stream with an inline image.
    /// </summary>
    private static MemoryStream CreateEmlWithInlineImage(
        string fileName, string mimeType, byte[] content, string contentId)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Test Sender", "sender@test.com"));
        message.To.Add(new MailboxAddress("Test Recipient", "recipient@test.com"));
        message.Subject = "Test Email with Inline Image";

        var builder = new BodyBuilder
        {
            HtmlBody = $"<html><body><p>See the image:</p><img src=\"cid:{contentId}\"/></body></html>"
        };

        var parts = mimeType.Split('/');
        var linkedResource = builder.LinkedResources.Add(fileName, content, new MimeKit.ContentType(parts[0], parts[1]));
        linkedResource.ContentId = contentId;

        message.Body = builder.ToMessageBody();

        var stream = new MemoryStream();
        message.WriteTo(stream);
        stream.Position = 0;
        return stream;
    }

    #endregion
}
