using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Email;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// spaarkeai-word-add-in-r1 task 116a: the server contract the Outlook add-in now RELIES ON, pinned.
/// </summary>
/// <remarks>
/// <para>A B2B guest's mailbox is in their home tenant, which the BFF's Graph call (on behalf of the user, through
/// Spaarke's tenant) cannot reach — a guest's save stored a header-only .eml and no attachment documents (owner UAT
/// 2026-10-08). The add-in therefore reads the body and the selected attachments through Office.js and sends them in
/// <c>email.body</c> / <c>email.attachments[].contentBase64</c>. No server code changed: the server already preferred
/// client content. These tests make that a contract instead of an accident:</para>
/// <list type="bullet">
/// <item>a request WITH a body never calls Graph (so a guest's save does not depend on it);</item>
/// <item>a request WITHOUT one still falls back to Graph (older add-ins), and keeps the request when Graph fails;</item>
/// <item>the .eml is built from the client's body and attachments, and the attachment extraction that creates the
/// documents finds every attachment by the name the client sent — including an attached email, which the add-in sends
/// as <c>application/octet-stream</c> under a <c>.eml</c> name;</item>
/// <item>the JSON the add-in sends binds onto <see cref="SaveRequest"/>.</item>
/// </list>
/// </remarks>
public class OfficeEmailClientContentTests
{
    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new AccessToken("fake-token", DateTimeOffset.UtcNow.AddHours(1)));
    }

    private static SaveRequest EmailSave(string? body, List<AttachmentReference>? attachments = null) => new()
    {
        ContentType = SaveContentType.Email,
        Email = new EmailMetadata
        {
            Subject = "Re: Filing",
            SenderEmail = "counsel@contoso.com",
            InternetMessageId = "<msg-1@contoso.com>",
            Body = body,
            IsBodyHtml = true,
            Attachments = attachments,
            SelectedAttachmentFileNames = attachments?.Select(a => a.FileName).ToList(),
        },
    };

    private static AttachmentReference Attachment(string fileName, string contentType, byte[] bytes) => new()
    {
        AttachmentId = Guid.NewGuid().ToString(),
        FileName = fileName,
        ContentType = contentType,
        Size = bytes.Length,
        ContentBase64 = Convert.ToBase64String(bytes),
    };

    [Fact]
    public async Task EnrichEmailFromGraph_ClientSentTheBody_NeverCallsGraph_AndKeepsTheClientContent()
    {
        var graph = new Mock<IGraphClientFactory>(MockBehavior.Strict);
        var enricher = new OfficeEmailEnricher(graph.Object, NullLogger<OfficeEmailEnricher>.Instance);
        var request = EmailSave("<p>The email text</p>", [Attachment("Contract.pdf", "application/pdf", [1, 2, 3])]);

        var result = await enricher.EnrichEmailFromGraphAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeSameAs(request, "client content is used as sent");
        graph.Verify(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never);
        graph.Verify(g => g.ForUserBetaAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnrichEmailFromGraph_NoBody_FallsBackToGraph_AndKeepsTheRequestWhenTheMailboxIsUnreachable()
    {
        // An older add-in sends no body. Graph is tried; for a guest it fails (home-tenant mailbox), and the save keeps
        // whatever the client sent rather than failing.
        var graph = new Mock<IGraphClientFactory>();
        graph.Setup(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("mailbox not reachable"));
        var enricher = new OfficeEmailEnricher(graph.Object, NullLogger<OfficeEmailEnricher>.Instance);
        var request = EmailSave(body: null);

        var result = await enricher.EnrichEmailFromGraphAsync(request, new DefaultHttpContext(), CancellationToken.None);

        graph.Verify(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeSameAs(request);
    }

    [Fact]
    public void BuildEml_FromClientContent_CarriesTheBody_AndEveryAttachmentIsExtractableByItsSentName()
    {
        var pdf = Encoding.UTF8.GetBytes("%PDF-1.7 contract");
        var forwarded = Encoding.UTF8.GetBytes("Subject: Fwd terms\r\nFrom: a@contoso.com\r\n\r\nForwarded body");
        var request = EmailSave(
            "<p>The email text</p>",
            [
                Attachment("Contract.pdf", "application/pdf", pdf),
                // How the add-in sends an attached Outlook email (emailContentCapture.ts): a .eml-named plain file part.
                Attachment("Fwd terms.eml", "application/octet-stream", forwarded),
            ]);

        using var eml = OfficeEmailEnricher.BuildEmlFromMetadata(request.Email!);

        var message = MimeMessage.Load(eml);
        message.HtmlBody.Should().Contain("The email text");
        eml.Position = 0;

        var converter = new EmailToEmlConverter(
            new HttpClient(),
            Options.Create(new EmailProcessingOptions()),
            new ConfigurationBuilder().Build(),
            new FakeTokenCredential(),
            NullLogger<EmailToEmlConverter>.Instance);
        var extracted = converter.ExtractAttachments(eml);

        extracted.Select(a => a.FileName).Should().BeEquivalentTo(request.Email!.SelectedAttachmentFileNames);
        ReadAll(extracted.Single(a => a.FileName == "Contract.pdf").Content!).Should().Equal(pdf);
        ReadAll(extracted.Single(a => a.FileName == "Fwd terms.eml").Content!).Should().Equal(forwarded);

        foreach (var attachment in extracted)
        {
            attachment.Content?.Dispose();
        }
    }

    [Fact]
    public void TheJsonTheAddInSends_BindsBodyAttachmentsAndNames()
    {
        // The exact field names useSaveFlow.ts / quickSaveHelpers.ts put on the wire (camelCase, web defaults).
        const string json = """
        {
          "contentType": "Email",
          "email": {
            "subject": "Re: Filing",
            "senderEmail": "counsel@contoso.com",
            "internetMessageId": "<msg-1@contoso.com>",
            "isNameSystemDerived": true,
            "body": "<p>The email text</p>",
            "isBodyHtml": true,
            "attachments": [
              { "attachmentId": "AAMk-1", "fileName": "Contract.pdf", "size": 3, "contentType": "application/pdf",
                "contentBase64": "YWJj", "isInline": false }
            ],
            "selectedAttachmentFileNames": ["Contract.pdf"]
          }
        }
        """;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        var request = JsonSerializer.Deserialize<SaveRequest>(json, options)!;

        request.Email!.Body.Should().Be("<p>The email text</p>");
        request.Email.IsBodyHtml.Should().BeTrue();
        request.Email.SelectedAttachmentFileNames.Should().Equal("Contract.pdf");
        var attachment = request.Email.Attachments.Should().ContainSingle().Subject;
        attachment.FileName.Should().Be("Contract.pdf");
        attachment.ContentBase64.Should().Be("YWJj");
        attachment.ContentType.Should().Be("application/pdf");
        attachment.Size.Should().Be(3);
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.Position = 0;
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
