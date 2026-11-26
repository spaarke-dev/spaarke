using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Infrastructure.Http;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Tests.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression;

/// <summary>
/// spaarke-intake-pipeline-r1 task 006, design §10.1 item 7. A server-side email send that carried <c>InReplyToMessageId</c> went out as
/// a NEW message (<c>sendMail</c>): the id was only stored for thread joining, so the recipient's mail client got no In-Reply-To or
/// References and the reply started a new conversation. Graph rejects a custom <c>internetMessageHeaders</c> entry that does not start
/// with <c>x-</c>, so the headers cannot be set by hand; Exchange sets them when the message is created with <c>createReply</c> on the
/// parent. <see cref="EmailChannelSender"/> now finds the parent in the sending mailbox, creates the reply draft, fills it in and sends
/// the draft. A parent that is not found fails with a stable problem code and sends nothing (it never falls back to a new message).
/// Each test drives the REAL sender over a recording Graph transport (the Graph SDK's HTTP boundary, as the sibling sender tests do) and
/// asserts the requests it makes.
/// SEEDED DEFECT (proof recorded in notes/006-threaded-reply.md): letting the reply branch fall back to <c>sendMail</c> when the parent is
/// missing fails <see cref="Reply_WhenTheParentIsNotFound_FailsWithTheProblemCode_AndSendsNothing"/>.
/// </summary>
public class Intake10_1_Item7_EmailChannelSenderThreadedReplyTests
{
    private const string Mailbox = "legal@contoso.com";
    private const string ParentRfcId = "<parent-1@mail.contoso.com>";
    private const string ParentGraphId = "PARENT-GRAPH-ID";
    private const string DraftId = "DRAFT-ID";

    // ── The reply branch ──────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reply_WhenTheParentExists_CreatesTheReplyOnTheParent_AndSendsTheDraft(bool userMode)
    {
        var transport = new Transport();

        var result = await Sender(transport, userMode).SendAsync(Request(userMode, inReplyTo: ParentRfcId));

        var root = userMode ? "/me" : $"/users/{Mailbox}";
        transport.Paths.Should().Contain($"POST {root}/messages/{ParentGraphId}/createReply");
        transport.Paths.Should().Contain($"PATCH {root}/messages/{DraftId}");
        transport.Paths.Should().Contain($"POST {root}/messages/{DraftId}/send");
        transport.Paths.Should().NotContain(p => p.EndsWith("/sendMail", StringComparison.OrdinalIgnoreCase),
            "a threaded reply is the draft's send; a new-message sendMail would start a new conversation");

        // The parent was found by its Internet-Message-Id in the sending mailbox.
        transport.Calls.Single(c => c.Method == "GET" && c.Path == $"{root}/messages").Query
            .Should().Contain("internetMessageId eq");

        // The draft carries the request's content, not the quoted original.
        var patch = JsonDocument.Parse(transport.Calls.Single(c => c.Method == "PATCH").Body!).RootElement;
        patch.Prop("subject").GetString().Should().Be("Re: Mutual NDA");
        patch.Prop("body").Prop("content").GetString().Should().Be("<p>Attached is the reviewed draft.</p>");
        patch.Prop("toRecipients").EnumerateArray().Single().Prop("emailAddress").Prop("address").GetString().Should().Be("requester@example.com");

        // The Internet-Message-Id of the sent reply is read back from Sent Items, as for any other send.
        result.ProviderMessageId.Should().Be("<sent-1@mail.contoso.com>");
    }

    [Fact]
    public async Task Reply_WhenTheParentIsNotFound_FailsWithTheProblemCode_AndSendsNothing()
    {
        var transport = new Transport { ParentExists = false };

        var act = () => Sender(transport, userMode: false).SendAsync(Request(userMode: false, inReplyTo: ParentRfcId));

        var failure = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        failure.Code.Should().Be(EmailChannelSender.ReplyParentNotFoundCode);
        failure.StatusCode.Should().Be(404);
        transport.Paths.Should().NotContain(p => p.Contains("createReply") || p.EndsWith("/send") || p.EndsWith("/sendMail"),
            "a reply that cannot be threaded is never sent, least of all as a new unthreaded message");
    }

    [Fact]
    public async Task Reply_WhenTheParentIsGivenAsAGraphId_IsFoundByIdAndRepliedTo()
    {
        var transport = new Transport();

        await Sender(transport, userMode: false).SendAsync(Request(userMode: false, inReplyTo: ParentGraphId));

        transport.Paths.Should().Contain($"GET /users/{Mailbox}/messages/{ParentGraphId}");
        transport.Paths.Should().Contain($"POST /users/{Mailbox}/messages/{ParentGraphId}/createReply");
    }

    [Fact]
    public async Task Reply_WhenAGraphIdParentIsNotFound_FailsWithTheProblemCode_AndSendsNothing()
    {
        var transport = new Transport { ParentExists = false };

        var act = () => Sender(transport, userMode: false).SendAsync(Request(userMode: false, inReplyTo: ParentGraphId));

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.Code.Should().Be(EmailChannelSender.ReplyParentNotFoundCode);
        transport.Paths.Should().NotContain(p => p.Contains("createReply") || p.EndsWith("/send") || p.EndsWith("/sendMail"));
    }

    // ── A send with no parent is unchanged ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Send_WithoutAnInReplyToMessageId_IsTheSameSendMailRequestAsBefore(string? inReplyTo)
    {
        var transport = new Transport();

        await Sender(transport, userMode: false).SendAsync(Request(userMode: false, inReplyTo: inReplyTo));

        transport.Paths.Where(p => !p.Contains("sentitems", StringComparison.OrdinalIgnoreCase))
            .Should().Equal($"POST /users/{Mailbox}/sendMail");
        // The exact request, as the sender built it before this task (Kiota writes the properties alphabetically).
        var sent = JsonNode.Parse(transport.Calls.Single(c => c.Path.EndsWith("/sendMail")).Body!);
        var expected = JsonNode.Parse("""{"Message":{"@odata.type":"#microsoft.graph.message","bccRecipients":[],"body":{"content":"<p>Attached is the reviewed draft.</p>","contentType":"html"},"ccRecipients":[],"from":{"emailAddress":{"address":"legal@contoso.com","name":"Legal"}},"subject":"Re: Mutual NDA","toRecipients":[{"emailAddress":{"address":"requester@example.com"}}]},"SaveToSentItems":true}""");
        JsonNode.DeepEquals(sent, expected).Should().BeTrue("the send request must be exactly what it was before the reply branch existed: {0}", sent!.ToJsonString());
    }

    // ── Headers, attachments, failures ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reply_WithAServerSendMarker_CarriesItOnTheReplyAsAnXHeader_AndNoOtherCustomHeader()
    {
        var transport = new Transport();

        await Sender(transport, userMode: false).SendAsync(
            Request(userMode: false, inReplyTo: ParentRfcId) with { ServerSendMarker = "signed-marker" });

        // The marker is not a patchable draft property, so it rides on the createReply request's message parameter.
        var createReply = JsonDocument.Parse(transport.Calls.Single(c => c.Path.EndsWith("/createReply")).Body!).RootElement;
        var headers = createReply.Prop("message").Prop("internetMessageHeaders").EnumerateArray().ToList();
        headers.Should().ContainSingle();
        headers[0].Prop("name").GetString().Should().Be(ServerSendMarker.HeaderName);
        headers[0].Prop("value").GetString().Should().Be("signed-marker");

        // Graph rejects a custom header without the x- prefix, and a PATCH of a draft does not take headers at all.
        transport.Calls.Single(c => c.Method == "PATCH").Body.Should().NotContainEquivalentOf("internetMessageHeaders");
        ServerSendMarker.HeaderName.Should().StartWith("x-");
    }

    [Fact]
    public async Task Reply_WithoutAServerSendMarker_AsksForNoHeaders()
    {
        var transport = new Transport();

        await Sender(transport, userMode: false).SendAsync(Request(userMode: false, inReplyTo: ParentRfcId));

        transport.Calls.Single(c => c.Path.EndsWith("/createReply")).Body.Should().NotContainEquivalentOf("internetMessageHeaders");
    }

    [Fact]
    public async Task Reply_WithAnAttachment_AddsItToTheDraft_BeforeTheDraftIsSent()
    {
        var transport = new Transport();
        var request = Request(userMode: false, inReplyTo: ParentRfcId) with
        {
            Attachments = [new ChannelAttachment { Name = "Redline.docx", Content = [1, 2, 3], ContentType = "application/octet-stream" }],
        };

        await Sender(transport, userMode: false).SendAsync(request);

        var attach = transport.Paths.IndexOf($"POST /users/{Mailbox}/messages/{DraftId}/attachments");
        var send = transport.Paths.IndexOf($"POST /users/{Mailbox}/messages/{DraftId}/send");
        attach.Should().BeGreaterThan(-1);
        send.Should().BeGreaterThan(attach, "an attachment added after the send would be lost");
        var body = JsonDocument.Parse(transport.Calls.Single(c => c.Path.EndsWith("/attachments")).Body!).RootElement;
        body.Prop("name").GetString().Should().Be("Redline.docx");
        body.Prop("contentBytes").GetString().Should().Be(Convert.ToBase64String([1, 2, 3]));
    }

    [Fact]
    public async Task Reply_WithAnAttachmentTooLargeForASingleRequest_FailsBeforeAnythingIsCreated()
    {
        var transport = new Transport();
        var request = Request(userMode: false, inReplyTo: ParentRfcId) with
        {
            Attachments = [new ChannelAttachment { Name = "Big.docx", Content = new byte[EmailChannelSender.MaxReplyAttachmentBytes + 1] }],
        };

        var act = () => Sender(transport, userMode: false).SendAsync(request);

        var failure = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        failure.Code.Should().Be(EmailChannelSender.ReplyAttachmentTooLargeCode);
        failure.StatusCode.Should().Be(413);
        transport.Paths.Should().BeEmpty("nothing was looked up, created or sent");
    }

    [Fact]
    public async Task Reply_ThatIsSystemMail_IsRefused_BecauseADraftSendAlwaysKeepsACopy()
    {
        var transport = new Transport();
        var request = Request(userMode: false, inReplyTo: ParentRfcId) with
        {
            Communication = new SendCommunicationRequest
            {
                To = ["invitee@example.com"],
                Subject = "Your invitation",
                Body = "<p>body</p>",
                InReplyToMessageId = ParentRfcId,
                SystemMail = new SystemMailOptions { StoredBody = "<p>stored</p>" },
            },
        };

        var act = () => Sender(transport, userMode: false).SendAsync(request);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.Code.Should().Be(EmailChannelSender.ReplySystemMailUnsupportedCode);
        transport.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task Reply_WhenTheDraftCannotBeFilledIn_SendsNothing_AndRemovesTheDraft()
    {
        var transport = new Transport { PatchStatus = HttpStatusCode.BadRequest };

        var act = () => Sender(transport, userMode: false).SendAsync(Request(userMode: false, inReplyTo: ParentRfcId));

        var failure = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        failure.Code.Should().Be("GRAPH_SEND_FAILED");
        failure.Extensions![EmailChannelSender.SendOutcomeExtension].Should().Be(EmailChannelSender.SendOutcomeNotSent);
        transport.Paths.Should().NotContain(p => p.EndsWith("/send") || p.EndsWith("/sendMail"));
        transport.Paths.Should().Contain($"DELETE /users/{Mailbox}/messages/{DraftId}");
    }

    [Fact]
    public async Task Reply_WhenTheDraftSendGets5xx_IsSentOnce_IsAnUnknownOutcome_AndKeepsTheDraft()
    {
        var transport = new Transport { SendStatus = HttpStatusCode.ServiceUnavailable };

        var act = () => Sender(transport, userMode: false).SendAsync(Request(userMode: false, inReplyTo: ParentRfcId));

        var failure = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        failure.Extensions![EmailChannelSender.SendOutcomeExtension].Should().Be(EmailChannelSender.SendOutcomeUnknown);
        transport.Paths.Count(p => p.EndsWith("/send")).Should().Be(1, "Graph may already have delivered it, so it is never sent again");
        transport.Paths.Should().NotContain(p => p.StartsWith("DELETE"), "a send of unknown outcome must not be undone");
    }

    // ── Harness ───────────────────────────────────────────────────────────────────────────────────────────────

    private static EmailChannelSender Sender(Transport transport, bool userMode)
    {
        var options = new GraphResilienceOptions
        {
            RetryCount = 3,
            RetryBackoffSeconds = 0,
            HonorRetryAfterHeader = false,
            TimeoutSeconds = 30,
            CircuitBreakerFailureThreshold = 100,
        };
        var handler = new GraphHttpMessageHandler(NullLogger<GraphHttpMessageHandler>.Instance, Options.Create(options)) { InnerHandler = transport };
        var client = new GraphServiceClient(new HttpClient(handler));
        var factory = new Mock<IGraphClientFactory>();
        if (userMode)
            factory.Setup(f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(client);
        else
            factory.Setup(f => f.ForApp()).Returns(client);
        return new EmailChannelSender(factory.Object, NullLogger<EmailChannelSender>.Instance);
    }

    private static ChannelSendRequest Request(bool userMode, string? inReplyTo) => new()
    {
        Communication = new SendCommunicationRequest
        {
            To = ["requester@example.com"],
            Subject = "Re: Mutual NDA",
            Body = "<p>Attached is the reviewed draft.</p>",
            InReplyToMessageId = inReplyTo,
        },
        FromAddress = Mailbox,
        FromDisplayName = userMode ? null : "Legal",
        UserMode = userMode,
        HttpContext = userMode ? new DefaultHttpContext() : null,
        CorrelationId = "task-006",
    };

    private sealed record Call(string Method, string Path, string Query, string? Body);

    /// <summary>A Graph mailbox that answers each request of the reply sequence and records it.</summary>
    private sealed class Transport : HttpMessageHandler
    {
        public List<Call> Calls { get; } = new();

        /// <summary>"METHOD /path" of every request, in order.</summary>
        public List<string> Paths => Calls.Select(c => $"{c.Method} {c.Path}").ToList();

        public bool ParentExists { get; init; } = true;
        public HttpStatusCode PatchStatus { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode SendStatus { get; init; } = HttpStatusCode.Accepted;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            var versionEnd = path.IndexOf('/', 1);
            path = path[versionEnd..]; // drop "/v1.0"
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add(new Call(request.Method.Method, path, Uri.UnescapeDataString(request.RequestUri.Query), body));

            var isSentItems = path.Contains("/mailFolders/sentitems/messages", StringComparison.OrdinalIgnoreCase);
            if (isSentItems)
                return Json(HttpStatusCode.OK, """{"value":[{"internetMessageId":"<sent-1@mail.contoso.com>"}]}""");
            if (path.EndsWith("/sendMail", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            if (path.EndsWith("/createReply", StringComparison.Ordinal))
                return Json(HttpStatusCode.Created, $$"""{"id":"{{DraftId}}"}""");
            if (path.EndsWith("/attachments", StringComparison.Ordinal))
                return Json(HttpStatusCode.Created, """{"id":"ATT-1"}""");
            if (path.EndsWith("/send", StringComparison.Ordinal))
                return Answer(SendStatus);
            if (request.Method == HttpMethod.Patch)
                return Answer(PatchStatus);
            if (request.Method == HttpMethod.Delete)
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            // The parent lookup: by filter (an Internet-Message-Id) or by id (a Graph id).
            if (path.EndsWith("/messages", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, ParentExists ? $$"""{"value":[{"id":"{{ParentGraphId}}"}]}""" : """{"value":[]}""");
            return ParentExists && path.EndsWith("/" + ParentGraphId, StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, $$"""{"id":"{{ParentGraphId}}"}""")
                : Json(HttpStatusCode.NotFound, """{"error":{"code":"ErrorItemNotFound","message":"not found"}}""");
        }

        private static HttpResponseMessage Answer(HttpStatusCode status) =>
            (int)status < 300
                ? Json(status, "{}")
                : Json(status, """{"error":{"code":"Failed","message":"failed"}}""");

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
