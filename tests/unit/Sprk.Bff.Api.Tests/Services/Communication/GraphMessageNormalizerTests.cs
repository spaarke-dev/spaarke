using FluentAssertions;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Domain tests for the Graph→envelope boundary mapper (task 011). These lock the mapping the
/// thread-continuity rung now depends on — In-Reply-To/References come from the message's internet
/// headers (no second Graph round-trip), plus conversationId, addresses, body, and attachments.
/// </summary>
public class GraphMessageNormalizerTests
{
    private readonly GraphMessageNormalizer _normalizer = new();

    [Fact]
    public void Normalize_MapsThreadHeadersAndCoreFields_FromInternetHeaders()
    {
        // Arrange
        var message = new Message
        {
            InternetMessageId = "<child@contoso.com>",
            Subject = "Re: Contract",
            ConversationId = "conv-123",
            ReceivedDateTime = DateTimeOffset.Parse("2026-07-15T10:00:00Z"),
            From = new Recipient { EmailAddress = new EmailAddress { Address = "sender@acme.com" } },
            ToRecipients = new List<Recipient>
            {
                new() { EmailAddress = new EmailAddress { Address = "shared@contoso.com" } }
            },
            CcRecipients = new List<Recipient>
            {
                new() { EmailAddress = new EmailAddress { Address = "watcher@contoso.com" } }
            },
            Body = new ItemBody { ContentType = BodyType.Html, Content = "<p>hi</p>" },
            InternetMessageHeaders = new List<InternetMessageHeader>
            {
                new() { Name = "In-Reply-To", Value = "<parent@contoso.com>" },
                new() { Name = "References", Value = "<a@contoso.com> <parent@contoso.com>" },
            },
        };

        // Act
        var envelope = _normalizer.Normalize(message, CommunicationDirection.Incoming);

        // Assert
        envelope.Direction.Should().Be(CommunicationDirection.Incoming);
        envelope.From.Should().Be("sender@acme.com");
        envelope.To.Should().ContainSingle().Which.Should().Be("shared@contoso.com");
        envelope.Cc.Should().ContainSingle().Which.Should().Be("watcher@contoso.com");
        envelope.Subject.Should().Be("Re: Contract");
        envelope.InternetMessageId.Should().Be("<child@contoso.com>");
        envelope.InReplyTo.Should().Be("<parent@contoso.com>");
        envelope.References.Should().Equal("<a@contoso.com>", "<parent@contoso.com>");
        envelope.ConversationId.Should().Be("conv-123");
        envelope.BodyHtml.Should().Be("<p>hi</p>");
        // BodyText is now the stripped plain-text of the HTML body (was null before the
        // classifier-body-blind fix), so the text-consuming rungs see the content.
        envelope.BodyText.Should().Be("hi");
        envelope.SentAt.Should().Be(DateTimeOffset.Parse("2026-07-15T10:00:00Z"));
    }

    [Fact]
    public void Normalize_WhenHtmlBody_PopulatesBodyTextFromStrippedHtml()
    {
        // The text-consuming rungs (AI classification, semantic match) read BodyText.
        // Before the fix, an HTML body left BodyText null → the classifier saw only the
        // subject line ("empty body" in the provenance). BodyText must now carry the text.
        var message = new Message
        {
            Subject = "New Matter",
            From = new Recipient { EmailAddress = new EmailAddress { Address = "s@acme.com" } },
            Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content =
                    "<html><head><style>p{color:red}</style></head><body>" +
                    "<p>Please open a <b>new&nbsp;matter</b>:</p>\r\n<ul><li>Smith v Smith</li></ul>" +
                    "</body></html>",
            },
        };

        var envelope = _normalizer.Normalize(message, CommunicationDirection.Incoming);

        envelope.BodyHtml.Should().Contain("<b>new");          // original markup preserved
        envelope.BodyText.Should().NotBeNullOrWhiteSpace();
        envelope.BodyText.Should().NotContain("<");            // tags stripped
        envelope.BodyText.Should().NotContain("color:red");    // <style> block dropped
        envelope.BodyText.Should().Contain("new matter");      // &nbsp; decoded + whitespace collapsed
        envelope.BodyText.Should().Contain("Smith v Smith");
    }

    [Fact]
    public void Normalize_WhenNoThreadHeaders_LeavesThreadFieldsEmpty()
    {
        // Arrange
        var message = new Message
        {
            Subject = "New enquiry",
            From = new Recipient { EmailAddress = new EmailAddress { Address = "sender@acme.com" } },
            Body = new ItemBody { ContentType = BodyType.Text, Content = "plain body" },
        };

        // Act
        var envelope = _normalizer.Normalize(message, CommunicationDirection.Incoming);

        // Assert
        envelope.InReplyTo.Should().BeNull();
        envelope.References.Should().BeEmpty();
        envelope.BodyText.Should().Be("plain body");
        envelope.BodyHtml.Should().BeNull();
    }

    [Fact]
    public void Normalize_MapsFileAttachmentMetadata_IncludingInlineFlag()
    {
        // Arrange
        var message = new Message
        {
            Subject = "With attachments",
            From = new Recipient { EmailAddress = new EmailAddress { Address = "sender@acme.com" } },
            Attachments = new List<Attachment>
            {
                new FileAttachment { Name = "contract.pdf", ContentType = "application/pdf", Size = 2048, IsInline = false },
                new FileAttachment { Name = "sig.png", ContentType = "image/png", Size = 64, IsInline = true },
            },
        };

        // Act
        var envelope = _normalizer.Normalize(message, CommunicationDirection.Incoming);

        // Assert
        envelope.Attachments.Should().HaveCount(2);
        envelope.Attachments[0].Name.Should().Be("contract.pdf");
        envelope.Attachments[0].SizeBytes.Should().Be(2048);
        envelope.Attachments[0].IsInline.Should().BeFalse();
        envelope.Attachments[1].Name.Should().Be("sig.png");
        envelope.Attachments[1].IsInline.Should().BeTrue();
    }
}
