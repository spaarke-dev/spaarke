using FluentAssertions;
using Microsoft.Graph.Models;
using MimeKit;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

public class GraphMessageToEmlConverterTests
{
    private readonly GraphMessageToEmlConverter _sut = new();

    #region Helpers

    private static Message CreateGraphMessage(
        string subject = "Test Subject",
        string from = "sender@contoso.com",
        string? fromName = null,
        string[]? to = null,
        string[]? cc = null,
        string[]? bcc = null,
        string? bodyContent = "<p>Hello World</p>",
        BodyType bodyType = BodyType.Html,
        string? internetMessageId = "<test-id@contoso.com>",
        DateTimeOffset? sentDateTime = null,
        DateTimeOffset? receivedDateTime = null,
        List<InternetMessageHeader>? headers = null,
        List<Attachment>? attachments = null)
    {
        to ??= ["recipient@example.com"];

        var message = new Message
        {
            Id = "graph-msg-abc123",
            Subject = subject,
            From = new Recipient
            {
                EmailAddress = new EmailAddress { Name = fromName ?? from, Address = from }
            },
            ToRecipients = to.Select(addr => new Recipient
            {
                EmailAddress = new EmailAddress { Name = addr, Address = addr }
            }).ToList(),
            Body = bodyContent != null
                ? new ItemBody { ContentType = bodyType, Content = bodyContent }
                : null,
            InternetMessageId = internetMessageId,
            SentDateTime = sentDateTime ?? new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero),
            ReceivedDateTime = receivedDateTime ?? new DateTimeOffset(2026, 3, 1, 10, 0, 5, TimeSpan.Zero),
            InternetMessageHeaders = headers,
            Attachments = attachments
        };

        if (cc != null)
        {
            message.CcRecipients = cc.Select(addr => new Recipient
            {
                EmailAddress = new EmailAddress { Name = addr, Address = addr }
            }).ToList();
        }

        if (bcc != null)
        {
            message.BccRecipients = bcc.Select(addr => new Recipient
            {
                EmailAddress = new EmailAddress { Name = addr, Address = addr }
            }).ToList();
        }

        return message;
    }

    private static FileAttachment CreateFileAttachment(
        string name = "document.pdf",
        string contentType = "application/pdf",
        byte[]? content = null,
        bool isInline = false,
        string? contentId = null)
    {
        return new FileAttachment
        {
            Name = name,
            ContentType = contentType,
            ContentBytes = content ?? [0x25, 0x50, 0x44, 0x46], // %PDF
            IsInline = isInline,
            ContentId = contentId
        };
    }

    private static MimeMessage ParseEml(EmlResult result)
    {
        using var stream = new MemoryStream(result.Content);
        return MimeMessage.Load(stream);
    }

    #endregion

    #region 1. Simple text message with all headers

    [Fact]
    public void ConvertToEml_SimpleMessage_ProducesValidEmlWithAllHeaders()
    {
        // Arrange
        var graphMessage = CreateGraphMessage(
            subject: "Quarterly Report",
            from: "alice@contoso.com",
            to: ["bob@example.com", "carol@example.com"],
            cc: ["dave@example.com"],
            bcc: ["eve@example.com"],
            bodyContent: "Plain text body",
            bodyType: BodyType.Text);

        // Act
        var result = _sut.ConvertToEml(graphMessage);

        // Assert
        result.Should().NotBeNull();
        result.Content.Should().NotBeEmpty();
        result.FileName.Should().EndWith(".eml");

        var parsed = ParseEml(result);
        parsed.Subject.Should().Be("Quarterly Report");
        parsed.From.Mailboxes.Should().ContainSingle()
            .Which.Address.Should().Be("alice@contoso.com");
        parsed.To.Mailboxes.Should().HaveCount(2);
        parsed.To.Mailboxes.Select(m => m.Address).Should().Contain("bob@example.com");
        parsed.To.Mailboxes.Select(m => m.Address).Should().Contain("carol@example.com");
        parsed.Cc.Mailboxes.Should().ContainSingle()
            .Which.Address.Should().Be("dave@example.com");
        parsed.Bcc.Mailboxes.Should().ContainSingle()
            .Which.Address.Should().Be("eve@example.com");
        parsed.MessageId.Should().Be("test-id@contoso.com");
    }

    #endregion

    #region 2. HTML message body

    [Fact]
    public void ConvertToEml_HtmlMessage_ContainsHtmlBody()
    {
        // Arrange
        var graphMessage = CreateGraphMessage(
            bodyContent: "<h1>Important</h1><p>Details here.</p>",
            bodyType: BodyType.Html);

        // Act
        var result = _sut.ConvertToEml(graphMessage);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert
        emlText.Should().Contain("text/html");

        var parsed = ParseEml(result);
        var textPart = parsed.Body as TextPart;
        textPart.Should().NotBeNull();
        textPart!.Text.Should().Contain("<h1>Important</h1>");
    }

    #endregion

    #region 3. File attachments produce multipart/mixed

    [Fact]
    public void ConvertToEml_WithFileAttachments_ProducesMultipartMixed()
    {
        // Arrange
        var graphMessage = CreateGraphMessage(
            attachments:
            [
                CreateFileAttachment("report.pdf", "application/pdf"),
                CreateFileAttachment("data.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    [0x50, 0x4B, 0x03, 0x04])
            ]);

        // Act
        var result = _sut.ConvertToEml(graphMessage);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert
        emlText.Should().Contain("multipart/mixed");
        emlText.Should().Contain("report.pdf");
        emlText.Should().Contain("data.xlsx");

        var parsed = ParseEml(result);
        parsed.Body.Should().BeOfType<Multipart>();
        var multipart = (Multipart)parsed.Body;
        // Body text part + 2 attachment parts
        multipart.Count.Should().Be(3);
    }

    #endregion

    #region 4. Inline images set ContentId

    [Fact]
    public void ConvertToEml_WithInlineImages_SetsContentIdAndInlineDisposition()
    {
        // Arrange
        var graphMessage = CreateGraphMessage(
            bodyContent: "<p>See image: <img src='cid:logo123' /></p>",
            bodyType: BodyType.Html,
            attachments:
            [
                CreateFileAttachment(
                    name: "logo.png",
                    contentType: "image/png",
                    content: [0x89, 0x50, 0x4E, 0x47],
                    isInline: true,
                    contentId: "logo123")
            ]);

        // Act
        var result = _sut.ConvertToEml(graphMessage);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert
        emlText.Should().Contain("multipart/related");
        emlText.Should().Contain("logo123");

        var parsed = ParseEml(result);
        // Find the inline attachment
        var parts = parsed.BodyParts.OfType<MimePart>().ToList();
        var inlinePart = parts.FirstOrDefault(p => p.FileName == "logo.png");
        inlinePart.Should().NotBeNull();
        inlinePart!.ContentDisposition.Disposition.Should().Be(ContentDisposition.Inline);
        inlinePart.ContentId.Should().Contain("logo123");
    }

    #endregion

    #region 5. Threading headers preserved

    [Fact]
    public void ConvertToEml_WithThreadingHeaders_PreservesInReplyToAndReferences()
    {
        // Arrange
        var graphMessage = CreateGraphMessage(
            headers:
            [
                new InternetMessageHeader { Name = "In-Reply-To", Value = "<parent-msg@contoso.com>" },
                new InternetMessageHeader
                {
                    Name = "References",
                    Value = "<root-msg@contoso.com> <parent-msg@contoso.com>"
                }
            ]);

        // Act
        var result = _sut.ConvertToEml(graphMessage);

        // Assert
        var parsed = ParseEml(result);
        // MimeKit strips angle brackets from InReplyTo and References
        parsed.InReplyTo.Should().Contain("parent-msg@contoso.com");
        parsed.References.Should().HaveCount(2);
        parsed.References.Should().Contain(r => r.Contains("root-msg@contoso.com"));
        parsed.References.Should().Contain(r => r.Contains("parent-msg@contoso.com"));
    }

    #endregion

    #region 6. Null body produces empty content

    [Fact]
    public void ConvertToEml_NullBody_ProducesValidEmlWithEmptyContent()
    {
        // Arrange
        var graphMessage = CreateGraphMessage(bodyContent: null);
        graphMessage.Body = null;

        // Act
        var result = _sut.ConvertToEml(graphMessage);

        // Assert
        result.Content.Should().NotBeEmpty(); // Still a valid .eml, just empty body

        var parsed = ParseEml(result);
        // Body should be parseable - the text content should be empty or whitespace
        var textPart = parsed.TextBody;
        (textPart ?? string.Empty).Trim().Should().BeEmpty();
    }

    #endregion

    #region 7. No from address falls back

    [Fact]
    public void ConvertToEml_NoFrom_UsesFallbackAddress()
    {
        // Arrange
        var graphMessage = CreateGraphMessage();
        graphMessage.From = null;

        // Act
        var result = _sut.ConvertToEml(graphMessage);

        // Assert
        var parsed = ParseEml(result);
        parsed.From.Mailboxes.Should().ContainSingle()
            .Which.Address.Should().Be("unknown@unknown.com");
    }

    #endregion

    #region Additional edge cases

    [Fact]
    public void ConvertToEml_MissingInternetMessageId_GeneratesFromGraphId()
    {
        // Arrange
        var graphMessage = CreateGraphMessage(internetMessageId: null);

        // Act
        var result = _sut.ConvertToEml(graphMessage);

        // Assert
        var parsed = ParseEml(result);
        parsed.MessageId.Should().Contain("graph.microsoft.com");
    }

    [Fact]
    public void ConvertToEml_MissingSentAndReceivedDateTime_UsesUtcNow()
    {
        // Arrange
        var graphMessage = CreateGraphMessage();
        graphMessage.SentDateTime = null;
        graphMessage.ReceivedDateTime = null;

        // Act
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var result = _sut.ConvertToEml(graphMessage);
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        // Assert - date should be approximately now (within a small window)
        var parsed = ParseEml(result);
        parsed.Date.Should().BeOnOrAfter(before);
        parsed.Date.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void ConvertToEml_WithBothInlineAndRegularAttachments_ProducesCorrectStructure()
    {
        // Arrange
        var graphMessage = CreateGraphMessage(
            attachments:
            [
                CreateFileAttachment("logo.png", "image/png", [0x89, 0x50], isInline: true, contentId: "cid1"),
                CreateFileAttachment("contract.pdf", "application/pdf")
            ]);

        // Act
        var result = _sut.ConvertToEml(graphMessage);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert - should have multipart/mixed containing multipart/related + regular attachment
        emlText.Should().Contain("multipart/mixed");
        emlText.Should().Contain("multipart/related");
        emlText.Should().Contain("contract.pdf");
        emlText.Should().Contain("logo.png");
    }

    [Fact]
    public void ConvertToEml_FileName_ContainsSanitizedSubjectAndDate()
    {
        // Arrange
        var graphMessage = CreateGraphMessage(
            subject: "RE: Invoice #123",
            sentDateTime: new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));

        // Act
        var result = _sut.ConvertToEml(graphMessage);

        // Assert
        result.FileName.Should().EndWith(".eml");
        result.FileName.Should().Contain("20260301_100000");
        // Invalid filename chars like the colon in "RE:" should be preserved since ':' is invalid on Windows
        // but the SanitizeFileName strips them
        result.FileName.Should().NotContainAny("<", ">", "\"");
    }

    [Fact]
    public void ConvertToEml_NullAttachmentsList_ProducesSinglePartBody()
    {
        // Arrange
        var graphMessage = CreateGraphMessage();
        graphMessage.Attachments = null;

        // Act
        var result = _sut.ConvertToEml(graphMessage);
        var emlText = System.Text.Encoding.UTF8.GetString(result.Content);

        // Assert
        emlText.Should().NotContain("multipart/mixed");
    }

    [Fact]
    public void ConvertToEml_EmptyBodyContent_ProducesValidEmlWithEmptyContent()
    {
        // Arrange
        var graphMessage = CreateGraphMessage();
        graphMessage.Body = new ItemBody { ContentType = BodyType.Html, Content = "" };

        // Act
        var result = _sut.ConvertToEml(graphMessage);

        // Assert
        result.Content.Should().NotBeEmpty();
        var parsed = ParseEml(result);
        // Body should be parseable - the text/html content should be empty or whitespace
        var htmlBody = parsed.HtmlBody;
        (htmlBody ?? string.Empty).Trim().Should().BeEmpty();
    }

    #endregion
}
