using System.Text;
using FluentAssertions;
using MimeKit;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior tests for <see cref="EmlToHtmlRenderer"/> — the authoritative server-side XSS boundary for the
/// .eml -> reading-pane HTML render (email-communication-solution-r5 task 010 / FR-07 / NFR-03).
///
/// These are pure-transformation tests over REAL <c>.eml</c> fixtures built with MimeKit (the same library that
/// wrote the archive, via <see cref="GraphMessageToEmlConverter"/>) — no HTTP, no Graph, no SPE, no mocks
/// (ADR-038: real fixtures, no <c>Mock&lt;HttpMessageHandler&gt;</c>). They exercise the security-critical
/// contract: HTML preservation, cid:->data: inline-image resolution, and neutralization of hostile markup.
/// </summary>
public class EmlToHtmlRendererTests
{
    private readonly EmlToHtmlRenderer _sut = new();

    // 1x1 transparent PNG (valid image bytes for a real inline part).
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static Stream ToEmlStream(MimeMessage message)
    {
        var ms = new MemoryStream();
        message.WriteTo(ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>Builds a self-contained multipart/related .eml (HTML body + one inline image with a Content-Id),
    /// mirroring GraphMessageToEmlConverter's inline-only output.</summary>
    private static MimeMessage BuildRelatedMessage(string htmlBody, string contentId, byte[] imageBytes, string imageMime = "image/png")
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alice", "alice@example.com"));
        message.To.Add(new MailboxAddress("Bob", "bob@example.com"));
        message.Subject = "Test";

        var html = new TextPart("html") { Text = htmlBody };
        var image = new MimePart(imageMime)
        {
            Content = new MimeContent(new MemoryStream(imageBytes)),
            ContentTransferEncoding = ContentEncoding.Base64,
            ContentId = contentId,
            ContentDisposition = new ContentDisposition(ContentDisposition.Inline)
        };

        var related = new Multipart("related") { html, image };
        message.Body = related;
        return message;
    }

    private static MimeMessage BuildHtmlOnlyMessage(string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alice", "alice@example.com"));
        message.To.Add(new MailboxAddress("Bob", "bob@example.com"));
        message.Subject = "Test";
        message.Body = new TextPart("html") { Text = htmlBody };
        return message;
    }

    private static MimeMessage BuildTextOnlyMessage(string textBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alice", "alice@example.com"));
        message.To.Add(new MailboxAddress("Bob", "bob@example.com"));
        message.Subject = "Test";
        message.Body = new TextPart("plain") { Text = textBody };
        return message;
    }

    [Fact]
    public async Task RenderSanitizedHtmlAsync_WithQuotedHistoryAndInlineCidImage_PreservesBodyAndResolvesToDataUri()
    {
        // Arrange — HTML with quoted history and an inline image referenced by cid:
        const string cid = "logo123@spaarke";
        var html =
            "<div>Latest reply with the <b>full body</b>." +
            "<img src=\"cid:logo123@spaarke\" alt=\"logo\" />" +
            "<blockquote>On Monday, Alice wrote: original quoted history preserved</blockquote></div>";
        using var stream = ToEmlStream(BuildRelatedMessage(html, cid, PngBytes));

        // Act
        var result = await _sut.RenderSanitizedHtmlAsync(stream);

        // Assert — body + quoted history preserved; cid resolved to an inline image data: URI; no cid left
        result.Should().Contain("full body");
        result.Should().Contain("original quoted history preserved");
        result.Should().Contain("data:image/png;base64,");
        result.Should().NotContain("cid:");
        result.Should().Contain("<img");
    }

    [Fact]
    public async Task RenderSanitizedHtmlAsync_WithScriptOnHandlerAndJavascriptScheme_NeutralizesAll()
    {
        // Arrange — hostile HTML: script tag, inline event handler, javascript: link
        var html =
            "<div>Hello<script>alert('xss')</script>" +
            "<img src=\"x\" onerror=\"alert(1)\">" +
            "<a href=\"javascript:alert(2)\">click</a></div>";
        using var stream = ToEmlStream(BuildHtmlOnlyMessage(html));

        // Act
        var result = await _sut.RenderSanitizedHtmlAsync(stream);

        // Assert — server-side sanitize is authoritative: no script, no on* handler, no javascript: scheme
        result.Should().NotContain("<script", "script tags must be stripped");
        result.Should().NotContain("onerror", "inline event handlers must be stripped");
        result.Should().NotContain("javascript:", "the javascript: scheme must be neutralized");
        result.Should().NotContain("alert(", "no executable payload should survive");
        result.Should().Contain("Hello", "benign text content is preserved");
    }

    [Fact]
    public async Task RenderSanitizedHtmlAsync_WithIframeAndObjectAndEmbed_RemovesDangerousTags()
    {
        // Arrange
        var html =
            "<div>Body<iframe src=\"https://evil.example\"></iframe>" +
            "<object data=\"x\"></object><embed src=\"y\"></div>";
        using var stream = ToEmlStream(BuildHtmlOnlyMessage(html));

        // Act
        var result = await _sut.RenderSanitizedHtmlAsync(stream);

        // Assert
        result.Should().NotContain("<iframe");
        result.Should().NotContain("<object");
        result.Should().NotContain("<embed");
        result.Should().Contain("Body");
    }

    [Fact]
    public async Task RenderSanitizedHtmlAsync_WithDataTextHtmlUri_StripsNonImageDataUri()
    {
        // Arrange — an attacker-supplied data:text/html URI must not survive (only image data: is allowed)
        var html = "<div><a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a></div>";
        using var stream = ToEmlStream(BuildHtmlOnlyMessage(html));

        // Act
        var result = await _sut.RenderSanitizedHtmlAsync(stream);

        // Assert — the non-image data: URI is dropped; no smuggled html/script payload
        result.Should().NotContain("data:text/html");
        result.Should().NotContain("<script");
    }

    [Fact]
    public async Task RenderSanitizedHtmlAsync_WithUnresolvedCidReference_DropsReferenceSafely()
    {
        // Arrange — cid: reference with NO matching inline part
        var html = "<div>Body<img src=\"cid:missing@nowhere\" alt=\"gone\"></div>";
        using var stream = ToEmlStream(BuildHtmlOnlyMessage(html));

        // Act
        var result = await _sut.RenderSanitizedHtmlAsync(stream);

        // Assert — unresolved cid: is dropped (not an allowed scheme); body still renders
        result.Should().NotContain("cid:");
        result.Should().Contain("Body");
    }

    [Fact]
    public async Task RenderSanitizedHtmlAsync_WithNoHtmlPart_SynthesizesHtmlFromTextPreservingLineBreaks()
    {
        // Arrange — plain-text-only email (no HTML part)
        var stream = ToEmlStream(BuildTextOnlyMessage("Line one\r\nLine two\r\nLine three"));

        // Act
        var result = await _sut.RenderSanitizedHtmlAsync(stream);
        stream.Dispose();

        // Assert — text preserved with line breaks converted to <br>; no raw injection
        result.Should().Contain("Line one");
        result.Should().Contain("Line two");
        result.Should().Contain("<br");
    }

    [Fact]
    public async Task RenderSanitizedHtmlAsync_WithTextContainingAngleBrackets_EncodesThemNotInjects()
    {
        // Arrange — plain-text body containing HTML-looking content must be encoded, not interpreted
        var stream = ToEmlStream(BuildTextOnlyMessage("2 < 3 and <script>alert(1)</script>"));

        // Act
        var result = await _sut.RenderSanitizedHtmlAsync(stream);
        stream.Dispose();

        // Assert — the angle-bracket text is encoded; no live script element is produced
        result.Should().NotContain("<script>alert(1)</script>");
        result.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public async Task RenderSanitizedHtmlAsync_WithHttpsAndMailtoLinks_KeepsAllowedSchemes()
    {
        // Arrange
        var html = "<div><a href=\"https://example.com/x\">web</a> <a href=\"mailto:x@example.com\">mail</a></div>";
        using var stream = ToEmlStream(BuildHtmlOnlyMessage(html));

        // Act
        var result = await _sut.RenderSanitizedHtmlAsync(stream);

        // Assert — allowed schemes survive
        result.Should().Contain("https://example.com/x");
        result.Should().Contain("mailto:x@example.com");
    }
}
