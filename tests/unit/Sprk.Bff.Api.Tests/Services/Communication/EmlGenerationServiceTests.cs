using FluentAssertions;
using Microsoft.Extensions.Logging;
using MimeKit;
using Moq;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

public class EmlGenerationServiceTests
{
    #region Test Infrastructure

    private readonly EmlGenerationService _sut;

    public EmlGenerationServiceTests()
    {
        _sut = new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>());
    }

    private static SendCommunicationRequest CreateRequest(
        string subject = "Test Subject",
        string body = "<p>Hello World</p>",
        BodyFormat bodyFormat = BodyFormat.HTML,
        string[]? to = null,
        string[]? cc = null,
        string[]? bcc = null) => new()
    {
        To = to ?? new[] { "recipient@example.com" },
        Subject = subject,
        Body = body,
        BodyFormat = bodyFormat,
        CommunicationType = CommunicationType.Email,
        Cc = cc,
        Bcc = bcc
    };

    private static SendCommunicationResponse CreateResponse(
        string from = "sender@contoso.com",
        string? graphMessageId = null,
        string? correlationId = null) => new()
    {
        GraphMessageId = graphMessageId ?? "test-msg-id",
        Status = CommunicationStatus.Send,
        SentAt = new DateTimeOffset(2026, 2, 21, 14, 30, 0, TimeSpan.Zero),
        From = from,
        CorrelationId = correlationId ?? "test-correlation"
    };

    #endregion

    #region Valid .eml Generation

    [Fact]
    public void GenerateEml_WithValidRequest_ProducesNonEmptyContent()
    {
        // Arrange
        var request = CreateRequest();
        var response = CreateResponse();

        // Act
        var result = _sut.GenerateEml(request, response);

        // Assert
        result.Should().NotBeNull();
        result.Content.Should().NotBeEmpty();
        result.Content.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GenerateEml_FileName_FollowsExpectedPattern()
    {
        // Arrange
        var request = CreateRequest(subject: "Invoice Report");
        var response = CreateResponse();

        // Act
        var result = _sut.GenerateEml(request, response);

        // Assert — filename should be "{sanitizedSubject}_{yyyyMMdd_HHmmss}.eml"
        result.FileName.Should().EndWith(".eml");
        result.FileName.Should().StartWith("Invoice Report_");
        result.FileName.Should().Contain("20260221_143000");
    }

    #endregion

    #region RFC 2822 Header Verification

    [Fact]
    public void GenerateEml_ContainsFromHeader()
    {
        // Arrange
        var request = CreateRequest();
        var response = CreateResponse(from: "noreply@contoso.com");

        // Act
        var result = _sut.GenerateEml(request, response);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert
        emlText.Should().Contain("From:");
        emlText.Should().Contain("noreply@contoso.com");
    }

    [Fact]
    public void GenerateEml_ContainsToHeader()
    {
        // Arrange
        var request = CreateRequest(to: new[] { "alice@example.com", "bob@example.com" });
        var response = CreateResponse();

        // Act
        var result = _sut.GenerateEml(request, response);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert
        emlText.Should().Contain("To:");
        emlText.Should().Contain("alice@example.com");
        emlText.Should().Contain("bob@example.com");
    }

    [Fact]
    public void GenerateEml_ContainsSubjectHeader()
    {
        // Arrange
        var request = CreateRequest(subject: "Quarterly Report Q4");
        var response = CreateResponse();

        // Act
        var result = _sut.GenerateEml(request, response);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert
        emlText.Should().Contain("Subject:");
        emlText.Should().Contain("Quarterly Report Q4");
    }

    [Fact]
    public void GenerateEml_ContainsDateHeader()
    {
        // Arrange
        var request = CreateRequest();
        var response = CreateResponse();

        // Act
        var result = _sut.GenerateEml(request, response);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert
        emlText.Should().Contain("Date:");
    }

    [Fact]
    public void GenerateEml_ContainsMimeVersionHeader()
    {
        // Arrange
        var request = CreateRequest();
        var response = CreateResponse();

        // Act
        var result = _sut.GenerateEml(request, response);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert
        emlText.Should().Contain("MIME-Version:");
    }

    #endregion

    #region HTML Body as MIME Part

    [Fact]
    public void GenerateEml_WithHtmlBody_ContainsTextHtmlContentType()
    {
        // Arrange
        var request = CreateRequest(
            body: "<h1>Important</h1><p>Details here.</p>",
            bodyFormat: BodyFormat.HTML);
        var response = CreateResponse();

        // Act
        var result = _sut.GenerateEml(request, response);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert — verify the content type is text/html
        emlText.Should().Contain("text/html");
    }

    [Fact]
    public void GenerateEml_WithPlainTextBody_ContainsTextPlainContentType()
    {
        // Arrange
        var request = CreateRequest(
            body: "This is plain text.",
            bodyFormat: BodyFormat.PlainText);
        var response = CreateResponse();

        // Act
        var result = _sut.GenerateEml(request, response);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert
        emlText.Should().Contain("text/plain");
    }

    #endregion

    #region CC/BCC Handling

    [Fact]
    public void GenerateEml_WithEmptyCcList_DoesNotIncludeCcHeader()
    {
        // Arrange
        var request = CreateRequest(cc: Array.Empty<string>());
        var response = CreateResponse();

        // Act
        var result = _sut.GenerateEml(request, response);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert — with empty CC, no Cc header should appear
        emlText.Should().NotContain("Cc:");
    }

    [Fact]
    public void GenerateEml_WithNullCcList_DoesNotIncludeCcHeader()
    {
        // Arrange
        var request = CreateRequest(cc: null);
        var response = CreateResponse();

        // Act
        var result = _sut.GenerateEml(request, response);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert
        emlText.Should().NotContain("Cc:");
    }

    [Fact]
    public void GenerateEml_WithCcRecipients_IncludesCcHeader()
    {
        // Arrange
        var request = CreateRequest(cc: new[] { "cc1@example.com", "cc2@example.com" });
        var response = CreateResponse();

        // Act
        var result = _sut.GenerateEml(request, response);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert
        emlText.Should().Contain("Cc:");
        emlText.Should().Contain("cc1@example.com");
        emlText.Should().Contain("cc2@example.com");
    }

    #endregion

    #region Special Characters in Subject

    [Fact]
    public void GenerateEml_WithSpecialCharactersInSubject_HandlesCorrectly()
    {
        // Arrange — subject with characters that need sanitization in filename
        var request = CreateRequest(subject: "RE: Invoice #123 - \"Urgent\" <Review>");
        var response = CreateResponse();

        // Act
        var result = _sut.GenerateEml(request, response);

        // Assert — .eml content should preserve the original subject in the header
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);
        emlText.Should().Contain("Subject:");

        // The filename should have invalid chars removed
        result.FileName.Should().EndWith(".eml");
        result.FileName.Should().NotContainAny("\"", "<", ">");
    }

    [Fact]
    public void GenerateEml_WithLongSubject_TruncatesFileName()
    {
        // Arrange — subject longer than 50 characters (SanitizeFileName truncates at 50)
        var longSubject = new string('A', 80);
        var request = CreateRequest(subject: longSubject);
        var response = CreateResponse();

        // Act
        var result = _sut.GenerateEml(request, response);

        // Assert — sanitized portion of filename should be at most 50 chars
        // Filename format: "{sanitized}_{dateStr}.eml"
        var nameWithoutDateAndExt = result.FileName.Split('_')[0];
        // Note: the subject itself can be up to 50 chars in the filename prefix
        nameWithoutDateAndExt.Length.Should().BeLessThanOrEqualTo(50);
    }

    #endregion

    #region Attachments in .eml

    [Fact]
    public void GenerateEml_WithAttachments_ProducesMultipartMixed()
    {
        // Arrange
        var request = CreateRequest();
        var response = CreateResponse();
        var attachments = new List<EmlAttachment>
        {
            new()
            {
                FileName = "report.pdf",
                Content = new byte[] { 0x25, 0x50, 0x44, 0x46 }, // %PDF
                ContentType = "application/pdf"
            }
        };

        // Act
        var result = _sut.GenerateEml(request, response, attachments);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert — multipart/mixed should be present
        emlText.Should().Contain("multipart/mixed");
        emlText.Should().Contain("report.pdf");
    }

    [Fact]
    public void GenerateEml_WithoutAttachments_ProducesSinglePartOutput()
    {
        // Arrange
        var request = CreateRequest();
        var response = CreateResponse();

        // Act
        var result = _sut.GenerateEml(request, response, attachments: null);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert — no multipart/mixed; single text part
        emlText.Should().NotContain("multipart/mixed");
        emlText.Should().Contain("text/html");
    }

    #endregion

    #region Parseable .eml Output

    [Fact]
    public void GenerateEml_OutputIsParseable_ByMimeKit()
    {
        // Arrange
        var request = CreateRequest(
            subject: "Parseable Test",
            to: new[] { "user1@example.com", "user2@example.com" },
            cc: new[] { "cc@example.com" });
        var response = CreateResponse(from: "sender@contoso.com");

        // Act
        var result = _sut.GenerateEml(request, response);

        // Assert — parse the .eml bytes back using MimeKit to verify well-formed RFC 2822
        using var stream = new MemoryStream(result.Content);
        var parsed = MimeMessage.Load(stream);

        parsed.Subject.Should().Be("Parseable Test");
        parsed.From.Mailboxes.Should().ContainSingle()
            .Which.Address.Should().Be("sender@contoso.com");
        parsed.To.Mailboxes.Should().HaveCount(2);
        parsed.Cc.Mailboxes.Should().ContainSingle()
            .Which.Address.Should().Be("cc@example.com");
    }

    #endregion
}
