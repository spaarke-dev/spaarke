using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public class TextExtractorServiceTests
{
    private readonly Mock<ILogger<TextExtractorService>> _loggerMock;
    private readonly IOptions<DocumentIntelligenceOptions> _options;
    private readonly TextExtractorService _service;

    public TextExtractorServiceTests()
    {
        _loggerMock = new Mock<ILogger<TextExtractorService>>();
        _options = Options.Create(new DocumentIntelligenceOptions());
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
        var options = Options.Create(new DocumentIntelligenceOptions
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
        var options = Options.Create(new DocumentIntelligenceOptions
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
        var options = Options.Create(new DocumentIntelligenceOptions
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
        var options = Options.Create(new DocumentIntelligenceOptions
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

    #region Email Extraction Tests

    [Fact]
    public async Task ExtractAsync_EmlFile_ReturnsEmailContent()
    {
        // Create a valid EML file using MimeKit
        using var emlStream = CreateEmlStream(
            subject: "Test Email Subject",
            from: "sender@example.com",
            to: "recipient@example.com",
            body: "This is the email body content for testing.");

        var result = await _service.ExtractAsync(emlStream, "test-email.eml");

        result.Success.Should().BeTrue();
        result.Method.Should().Be(TextExtractionMethod.Email);
        result.Text.Should().Contain("EMAIL MESSAGE");
        result.Text.Should().Contain("Test Email Subject");
        result.Text.Should().Contain("sender@example.com");
        result.Text.Should().Contain("recipient@example.com");
        result.Text.Should().Contain("This is the email body content");
    }

    [Fact]
    public async Task ExtractAsync_EmlFile_WithHtmlBody_ExtractsPlainText()
    {
        // Create EML with HTML body only
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Sender", "sender@example.com"));
        message.To.Add(new MailboxAddress("Recipient", "recipient@example.com"));
        message.Subject = "HTML Email Test";
        message.Body = new TextPart("html")
        {
            Text = "<html><body><p>Hello <strong>World</strong>!</p></body></html>"
        };

        using var stream = new MemoryStream();
        await message.WriteToAsync(stream);
        stream.Position = 0;

        var result = await _service.ExtractAsync(stream, "html-email.eml");

        result.Success.Should().BeTrue();
        result.Text.Should().Contain("Hello");
        result.Text.Should().Contain("World");
        result.Text.Should().NotContain("<strong>");
    }

    [Fact]
    public async Task ExtractAsync_EmlFile_WithCcRecipients_IncludesCc()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Sender", "sender@example.com"));
        message.To.Add(new MailboxAddress("Recipient", "to@example.com"));
        message.Cc.Add(new MailboxAddress("CC Person", "cc@example.com"));
        message.Subject = "Email with CC";
        message.Body = new TextPart("plain") { Text = "Body text" };

        using var stream = new MemoryStream();
        await message.WriteToAsync(stream);
        stream.Position = 0;

        var result = await _service.ExtractAsync(stream, "cc-email.eml");

        result.Success.Should().BeTrue();
        result.Text.Should().Contain("cc@example.com");
    }

    [Fact]
    public async Task ExtractAsync_MsgFile_WithInvalidFormat_ReturnsError()
    {
        // MSG files require specific OLE2 format - invalid data should fail gracefully
        using var stream = CreateStream("This is not a valid MSG file format");

        var result = await _service.ExtractAsync(stream, "invalid.msg");

        result.Success.Should().BeFalse();
        result.Method.Should().Be(TextExtractionMethod.Email);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(".eml")]
    [InlineData(".msg")]
    [InlineData(".EML")]
    [InlineData(".MSG")]
    public void IsSupported_EmailType_ReturnsTrue(string extension)
    {
        var result = _service.IsSupported(extension);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(".eml")]
    [InlineData(".msg")]
    public void GetMethod_EmailType_ReturnsEmail(string extension)
    {
        var result = _service.GetMethod(extension);

        result.Should().Be(ExtractionMethod.Email);
    }

    [Theory]
    [InlineData(".eml")]
    [InlineData(".msg")]
    public async Task ExtractAsync_EmailFile_ExceedsMaxSize_ReturnsFailed(string extension)
    {
        var options = Options.Create(new DocumentIntelligenceOptions
        {
            MaxFileSizeBytes = 100 // 100 bytes max
        });
        var service = new TextExtractorService(options, _loggerMock.Object);

        var content = new string('a', 200); // 200 bytes > 100 bytes limit
        using var stream = CreateStream(content);

        var result = await service.ExtractAsync(stream, $"large{extension}");

        result.Success.Should().BeFalse();
        result.Method.Should().Be(TextExtractionMethod.Email);
        result.ErrorMessage.Should().Contain("exceeds maximum");
    }

    [Fact]
    public async Task ExtractAsync_EmlFile_EmptyBody_ReturnsHeadersOnly()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Sender", "sender@example.com"));
        message.To.Add(new MailboxAddress("Recipient", "recipient@example.com"));
        message.Subject = "Email with no body";
        // No body set

        using var stream = new MemoryStream();
        await message.WriteToAsync(stream);
        stream.Position = 0;

        var result = await _service.ExtractAsync(stream, "no-body.eml");

        // Should still succeed with headers, even if body is empty
        result.Success.Should().BeTrue();
        result.Text.Should().Contain("Email with no body");
        result.Text.Should().Contain("sender@example.com");
    }

    [Fact]
    public async Task ExtractAsync_EmlFile_LargeContent_TruncatesContent()
    {
        var options = Options.Create(new DocumentIntelligenceOptions
        {
            MaxInputTokens = 100 // Very small limit for testing
        });
        var service = new TextExtractorService(options, _loggerMock.Object);

        // Create EML with large body
        var largeBody = new string('x', 1000);
        using var emlStream = CreateEmlStream(
            subject: "Large Email",
            from: "sender@example.com",
            to: "recipient@example.com",
            body: largeBody);

        var result = await service.ExtractAsync(emlStream, "large-email.eml");

        result.Success.Should().BeTrue();
        result.Text.Should().Contain("[Content truncated");
    }

    /// <summary>
    /// Helper method to create a valid EML stream using MimeKit
    /// </summary>
    private static MemoryStream CreateEmlStream(string subject, string from, string to, string body)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Date = DateTimeOffset.UtcNow;
        message.Body = new TextPart("plain") { Text = body };

        var stream = new MemoryStream();
        message.WriteTo(stream);
        stream.Position = 0;
        return stream;
    }

    #endregion

    #region Email Metadata Tests

    [Fact]
    public async Task ExtractAsync_EmlFile_ReturnsEmailMetadata()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Sender Name", "sender@example.com"));
        message.To.Add(new MailboxAddress("Recipient Name", "recipient@example.com"));
        message.Subject = "Test Email Subject";
        message.Date = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        message.Body = new TextPart("plain") { Text = "This is the email body content." };

        using var stream = new MemoryStream();
        await message.WriteToAsync(stream);
        stream.Position = 0;

        var result = await _service.ExtractAsync(stream, "test.eml");

        result.Success.Should().BeTrue();
        result.IsEmail.Should().BeTrue();
        result.EmailMetadata.Should().NotBeNull();
        result.EmailMetadata!.Subject.Should().Be("Test Email Subject");
        result.EmailMetadata.From.Should().Contain("sender@example.com");
        result.EmailMetadata.To.Should().Contain("recipient@example.com");
        // Date is converted to local time, so just check date part
        result.EmailMetadata.Date.Should().HaveYear(2024);
        result.EmailMetadata.Date.Should().HaveMonth(6);
        result.EmailMetadata.Date.Should().HaveDay(15);
        result.EmailMetadata.Body.Should().Contain("This is the email body content.");
    }

    [Fact]
    public async Task ExtractAsync_EmlFile_WithCc_IncludesCcInMetadata()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Sender", "sender@example.com"));
        message.To.Add(new MailboxAddress("To Person", "to@example.com"));
        message.Cc.Add(new MailboxAddress("CC Person", "cc@example.com"));
        message.Subject = "Email with CC";
        message.Body = new TextPart("plain") { Text = "Body text" };

        using var stream = new MemoryStream();
        await message.WriteToAsync(stream);
        stream.Position = 0;

        var result = await _service.ExtractAsync(stream, "cc-email.eml");

        result.EmailMetadata.Should().NotBeNull();
        result.EmailMetadata!.Cc.Should().Contain("cc@example.com");
    }

    [Fact]
    public async Task ExtractAsync_EmlFile_WithAttachment_IncludesAttachmentInMetadata()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Sender", "sender@example.com"));
        message.To.Add(new MailboxAddress("Recipient", "recipient@example.com"));
        message.Subject = "Email with Attachment";

        var multipart = new Multipart("mixed");
        multipart.Add(new TextPart("plain") { Text = "Email body with attachment." });

        var attachment = new MimePart("application", "pdf")
        {
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes("fake pdf content"))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            FileName = "document.pdf"
        };
        multipart.Add(attachment);

        message.Body = multipart;

        using var stream = new MemoryStream();
        await message.WriteToAsync(stream);
        stream.Position = 0;

        var result = await _service.ExtractAsync(stream, "attachment-email.eml");

        result.Success.Should().BeTrue();
        result.EmailMetadata.Should().NotBeNull();
        result.EmailMetadata!.HasAttachments.Should().BeTrue();
        result.EmailMetadata.Attachments.Should().HaveCount(1);
        result.EmailMetadata.Attachments[0].Filename.Should().Be("document.pdf");
        result.EmailMetadata.Attachments[0].MimeType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task ExtractAsync_EmlFile_WithMultipleAttachments_IncludesAllAttachments()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Sender", "sender@example.com"));
        message.To.Add(new MailboxAddress("Recipient", "recipient@example.com"));
        message.Subject = "Email with Multiple Attachments";

        var multipart = new Multipart("mixed");
        multipart.Add(new TextPart("plain") { Text = "Email body with multiple attachments." });

        // Add first attachment
        var attachment1 = new MimePart("application", "pdf")
        {
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes("pdf content"))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            FileName = "document.pdf"
        };
        multipart.Add(attachment1);

        // Add second attachment
        var attachment2 = new MimePart("image", "png")
        {
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes("image content"))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            FileName = "image.png"
        };
        multipart.Add(attachment2);

        message.Body = multipart;

        using var stream = new MemoryStream();
        await message.WriteToAsync(stream);
        stream.Position = 0;

        var result = await _service.ExtractAsync(stream, "multiple-attachments.eml");

        result.EmailMetadata.Should().NotBeNull();
        result.EmailMetadata!.Attachments.Should().HaveCount(2);
        result.EmailMetadata.Attachments.Should().Contain(a => a.Filename == "document.pdf");
        result.EmailMetadata.Attachments.Should().Contain(a => a.Filename == "image.png");
    }

    [Fact]
    public async Task ExtractAsync_EmlFile_WithInlineAttachment_SetsIsInlineFlag()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Sender", "sender@example.com"));
        message.To.Add(new MailboxAddress("Recipient", "recipient@example.com"));
        message.Subject = "Email with Inline Image";

        var multipart = new Multipart("related");
        multipart.Add(new TextPart("html") { Text = "<html><body><img src=\"cid:image1\"/></body></html>" });

        var inlineImage = new MimePart("image", "png")
        {
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes("image content"))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
            ContentId = "image1",
            FileName = "inline-image.png"
        };
        multipart.Add(inlineImage);

        message.Body = multipart;

        using var stream = new MemoryStream();
        await message.WriteToAsync(stream);
        stream.Position = 0;

        var result = await _service.ExtractAsync(stream, "inline-image.eml");

        result.EmailMetadata.Should().NotBeNull();
        result.EmailMetadata!.Attachments.Should().Contain(a =>
            a.Filename == "inline-image.png" && a.IsInline && a.ContentId == "image1");
    }

    [Fact]
    public async Task ExtractAsync_EmlFile_TruncatesLargeBody()
    {
        var options = Options.Create(new DocumentIntelligenceOptions
        {
            MaxInputTokens = 100000 // Large enough to not truncate the AI text
        });
        var service = new TextExtractorService(options, _loggerMock.Object);

        // Create email with body larger than 10K chars
        var largeBody = new string('x', 15000);
        using var emlStream = CreateEmlStream(
            subject: "Large Body Email",
            from: "sender@example.com",
            to: "recipient@example.com",
            body: largeBody);

        var result = await service.ExtractAsync(emlStream, "large-body.eml");

        result.Success.Should().BeTrue();
        result.EmailMetadata.Should().NotBeNull();
        result.EmailMetadata!.Body.Should().HaveLength(10000 + "\n\n[Content truncated]".Length);
        result.EmailMetadata.Body.Should().EndWith("[Content truncated]");
    }

    [Fact]
    public async Task ExtractAsync_EmlFile_NoAttachments_HasAttachmentsFalse()
    {
        using var emlStream = CreateEmlStream(
            subject: "Simple Email",
            from: "sender@example.com",
            to: "recipient@example.com",
            body: "No attachments here.");

        var result = await _service.ExtractAsync(emlStream, "simple.eml");

        result.EmailMetadata.Should().NotBeNull();
        result.EmailMetadata!.HasAttachments.Should().BeFalse();
        result.EmailMetadata.Attachments.Should().BeEmpty();
    }

    #endregion

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
        var options = Options.Create(new DocumentIntelligenceOptions
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
        var options = Options.Create(new DocumentIntelligenceOptions
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

    [Fact]
    public void SucceededWithEmail_CreatesEmailResult()
    {
        var metadata = new EmailMetadata
        {
            Subject = "Test Subject",
            From = "sender@example.com",
            To = "recipient@example.com",
            Date = new DateTime(2024, 6, 15),
            Body = "Email body content"
        };

        var result = TextExtractionResult.SucceededWithEmail("formatted text", metadata);

        result.Success.Should().BeTrue();
        result.Text.Should().Be("formatted text");
        result.Method.Should().Be(TextExtractionMethod.Email);
        result.EmailMetadata.Should().BeSameAs(metadata);
        result.IsEmail.Should().BeTrue();
    }

    [Fact]
    public void IsEmail_ReturnsTrue_WhenEmailMethodWithMetadata()
    {
        var metadata = new EmailMetadata { Subject = "Test" };
        var result = TextExtractionResult.SucceededWithEmail("text", metadata);

        result.IsEmail.Should().BeTrue();
    }

    [Fact]
    public void IsEmail_ReturnsFalse_WhenNativeMethod()
    {
        var result = TextExtractionResult.Succeeded("content", TextExtractionMethod.Native);

        result.IsEmail.Should().BeFalse();
    }

    [Fact]
    public void IsEmail_ReturnsFalse_WhenEmailMethodButNoMetadata()
    {
        // Edge case: Email method but EmailMetadata is null
        var result = new TextExtractionResult
        {
            Success = true,
            Text = "text",
            Method = TextExtractionMethod.Email,
            EmailMetadata = null
        };

        result.IsEmail.Should().BeFalse();
    }
}
