using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// ebilling-r1 task 151c-1a, D2 b (uac-r2 F2): a server-only send carries the signed <see cref="ServerSendMarker"/> header, and the inbound
/// mailbox path (<see cref="IncomingCommunicationProcessor"/>) skips attachment documents and the <c>.eml</c> archive ONLY for a message whose
/// marker this server signed for exactly the attachments the message carries. Unmarked, forged, copied-onto-other-files and key-less cases
/// are processed exactly as before.
/// </summary>
public class ServerSendMarkerTests
{
    private static readonly byte[] Csv = Encoding.UTF8.GetBytes("BatchId,InvoiceNumber\r\nb1,INV-A\r\n");
    private static readonly byte[] Json = Encoding.UTF8.GetBytes("""{"invoices":[{"invoiceNumber":"INV-A"}]}""");
    private static readonly Guid BatchId = Guid.Parse("15100000-0000-4000-8000-0000000000b1");

    private static List<(string? Name, byte[]? Content)> Files() => [("ap-export-b1.csv", Csv), ("ap-export-b1.json", Json)];

    private static FixedKeyTrackingTokenSigner Server() => new(FixedKeyTrackingTokenSigner.KeyA);

    private static async Task<string> MarkerFor(FixedKeyTrackingTokenSigner signer, IEnumerable<(string? Name, byte[]? Content)> files) =>
        (await ServerSendMarker.SignAsync(signer, files, BatchId, DateTimeOffset.UtcNow, CancellationToken.None))!;

    // ---------------------------------------------------------------------------------------------------------------------------------
    // The marker itself
    // ---------------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "151c-1a D2 b: a marker verifies for the files it was signed for, in any order and with folding whitespace")]
    public async Task Marker_VerifiesForItsOwnFiles()
    {
        var signer = Server();
        var marker = await MarkerFor(signer, Files());
        var folded = marker[..20] + "\r\n " + marker[20..];

        (await ServerSendMarker.IsVerifiedAsync(signer, [marker], Files(), CancellationToken.None)).Should().BeTrue();
        (await ServerSendMarker.IsVerifiedAsync(signer, [folded], Enumerable.Reverse(Files()), CancellationToken.None)).Should().BeTrue();
    }

    public static IEnumerable<object[]> Unverified()
    {
        yield return ["forged: signed with another key"];
        yield return ["copied onto other files"];
        yield return ["one file dropped"];
        yield return ["a tracking-footer token for a record"];
        yield return ["two marker headers"];
        yield return ["no attachments"];
        yield return ["no signer on the inbound side"];
        yield return ["no key on the inbound side"];
        yield return ["garbage"];
    }

    [Theory(DisplayName = "151c-1a D2 b must-not-fire: a forged, copied, foreign, doubled, unbound or unverifiable marker is never honoured")]
    [MemberData(nameof(Unverified))]
    public async Task Marker_IsNotHonoured(string kind)
    {
        var signer = Server();
        var valid = await MarkerFor(signer, Files());
        List<string> markers = [valid];
        List<(string? Name, byte[]? Content)> files = Files();
        Sprk.Bff.Api.Services.Communication.Tracking.ITrackingTokenSigner? verifier = signer;
        switch (kind)
        {
            case "forged: signed with another key": markers = [await MarkerFor(new FixedKeyTrackingTokenSigner(FixedKeyTrackingTokenSigner.KeyB), Files())]; break;
            case "copied onto other files": files = [("invoice.pdf", Encoding.UTF8.GetBytes("%PDF-1.7 genuine inbound"))]; break;
            case "one file dropped": files = Files().Take(1).ToList(); break;
            case "a tracking-footer token for a record": markers = [(await signer.SignAsync("sprk_matter", Guid.NewGuid(), null, DateTimeOffset.UtcNow))!]; break;
            case "two marker headers": markers = [valid, valid]; break;
            case "no attachments": files = []; break;
            case "no signer on the inbound side": verifier = null; break;
            case "no key on the inbound side": verifier = new FixedKeyTrackingTokenSigner(null); break;
            default: markers = ["not.a.token"]; break;
        }

        (await ServerSendMarker.IsVerifiedAsync(verifier, markers, files, CancellationToken.None)).Should().BeFalse();
    }

    [Fact(DisplayName = "151c-1a D2 b: no signing key, or no files, signs no marker")]
    public async Task NoKeyOrNoFiles_SignsNothing()
    {
        (await ServerSendMarker.SignAsync(new FixedKeyTrackingTokenSigner(null), Files(), BatchId, DateTimeOffset.UtcNow, CancellationToken.None)).Should().BeNull();
        (await ServerSendMarker.SignAsync(Server(), [], BatchId, DateTimeOffset.UtcNow, CancellationToken.None)).Should().BeNull();
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // The email channel sends it as a header
    // ---------------------------------------------------------------------------------------------------------------------------------

    [Theory(DisplayName = "151c-1a D2 b: the email channel sends the marker as the x-spaarke-server-send header, and no header without one")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmailChannel_SendsTheMarkerHeader(bool marked)
    {
        var transport = new Transport();
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Returns(new GraphServiceClient(new HttpClient(transport)));
        var sender = new EmailChannelSender(factory.Object, NullLogger<EmailChannelSender>.Instance);

        await sender.SendAsync(new ChannelSendRequest
        {
            Communication = new SendCommunicationRequest { To = ["ap@example.com"], Subject = "AP export", Body = "b" },
            FromAddress = "noreply@contoso.com",
            UserMode = false,
            CorrelationId = "151c-1a",
            ServerSendMarker = marked ? "token.value" : null,
        });

        var body = transport.Bodies.Single(b => b.Path.EndsWith("/sendMail", StringComparison.OrdinalIgnoreCase)).Body!;
        var message = JsonDocument.Parse(body).RootElement.EnumerateObject().Single(p => p.NameEquals("Message") || p.NameEquals("message")).Value;
        var headers = message.EnumerateObject().Where(p => string.Equals(p.Name, "internetMessageHeaders", StringComparison.OrdinalIgnoreCase)).ToList();
        if (marked)
        {
            var header = headers.Should().ContainSingle().Subject.Value.EnumerateArray().Should().ContainSingle().Subject;
            header.GetProperty("name").GetString().Should().Be(ServerSendMarker.HeaderName);
            header.GetProperty("value").GetString().Should().Be("token.value");
        }
        else
        {
            headers.Should().BeEmpty();
        }
    }

    private sealed class Transport : HttpMessageHandler
    {
        public List<(string Path, string? Body)> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add((request.RequestUri!.AbsolutePath, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return request.RequestUri.AbsolutePath.EndsWith("/sendMail", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.Accepted)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"value":[]}""", Encoding.UTF8, "application/json") };
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // The inbound mailbox path honours it, and only it
    // ---------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The scoped services the attachment step and the archive step ask for; nothing is returned, so each step stops (non-fatally) at its first ask.</summary>
    private sealed class RecordingProvider : IServiceProvider
    {
        public List<Type> Requested { get; } = [];

        public object? GetService(Type serviceType)
        {
            Requested.Add(serviceType);
            return null;
        }
    }

    private sealed class FixedResponse(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private const string Mailbox = "mailbox-central@spaarke.com";
    private const string GraphMessageId = "AAMkAGE1M2IyNGNm-server-send-001";

    private static string MessageJson(string? marker, IEnumerable<(string? Name, byte[]? Content)> files) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["id"] = GraphMessageId,
        ["internetMessageId"] = null,
        ["internetMessageHeaders"] = marker is null
            ? new[] { new { name = "X-MS-Exchange-Organization-AuthAs", value = "Internal" } }
            : new[] { new { name = "X-MS-Exchange-Organization-AuthAs", value = "Internal" }, new { name = "X-Spaarke-Server-Send", value = marker } },
        ["from"] = new { emailAddress = new { name = "Finance User", address = "finance.user@contoso.com" } },
        ["toRecipients"] = new[] { new { emailAddress = new { name = "AP", address = Mailbox } } },
        ["ccRecipients"] = Array.Empty<object>(),
        ["subject"] = "AP export b1: 1 invoice",
        ["body"] = new { contentType = "text", content = "AP export batch b1" },
        ["uniqueBody"] = new { contentType = "text", content = "AP export batch b1" },
        ["receivedDateTime"] = DateTimeOffset.UtcNow.ToString("o"),
        ["hasAttachments"] = true,
        ["attachments"] = files.Select(f => new Dictionary<string, object?>
        {
            ["@odata.type"] = "#microsoft.graph.fileAttachment",
            ["name"] = f.Name,
            ["contentType"] = "application/octet-stream",
            ["size"] = f.Content!.Length,
            ["contentBytes"] = Convert.ToBase64String(f.Content!),
        }).ToArray(),
    });

    private static (IncomingCommunicationProcessor Processor, RecordingProvider Scoped, Mock<IDataverseService> Dv, Mock<ITextExtractor> Text) Inbound(
        string messageJson, Sprk.Bff.Api.Services.Communication.Tracking.ITrackingTokenSigner? signer)
    {
        var graph = new Mock<IGraphClientFactory>();
        graph.Setup(f => f.ForApp()).Returns(new GraphServiceClient(new HttpClient(new FixedResponse(messageJson))));

        var dv = new Mock<IDataverseService>();
        var text = new Mock<ITextExtractor>();
        text.Setup(t => t.IsSupported(It.IsAny<string>())).Returns(true);
        text.Setup(t => t.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextExtractionResult.Succeeded("Matter Alpha 1,250.00", TextExtractionMethod.Native));
        var account = new Entity("sprk_communicationaccount", Guid.NewGuid())
        {
            ["sprk_emailaddress"] = Mailbox,
            ["sprk_name"] = "Central Mailbox",
            ["sprk_receiveenabled"] = true,
            ["sprk_autocreaterecords"] = true, // the attachment step runs for this mailbox
            ["sprk_accounttype"] = new OptionSetValue(100000000),
        };
        dv.Setup(d => d.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([account]);
        dv.Setup(d => d.CreateCommunicationRaceProofAsync(It.IsAny<Entity>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid.NewGuid(), false));

        var scoped = new RecordingProvider();
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(scoped);
        var scopes = new Mock<IServiceScopeFactory>();
        scopes.Setup(f => f.CreateScope()).Returns(scope.Object);

        var accounts = new CommunicationAccountService(dv.Object, dv.Object, Mock.Of<IDistributedCache>(), Mock.Of<ILogger<CommunicationAccountService>>());
        var jobOptions = new Mock<IOptions<ServiceBusOptions>>();
        jobOptions.Setup(o => o.Value).Returns(new ServiceBusOptions());
        var jobs = new Mock<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>(
            MockBehavior.Loose, jobOptions.Object, Mock.Of<ILogger<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>>(),
            new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object).Object;

        var processor = new IncomingCommunicationProcessor(
            graph.Object,
            dv.Object,
            dv.Object,
            accounts,
            new IncomingAssociationResolver(
                new IAssociationRung[] { new ExplicitReferenceRung(dv.Object), new ThreadContinuityRung(dv.Object), new ParticipantCorrelationRung(dv.Object) },
                dv.Object,
                dv.Object,
                AssociationTestSupport.Mapper(),
                CoreAncestorResolverFixtures.Inert(),
                new RecordOwnershipResolverDouble(),
                Mock.Of<ILogger<IncomingAssociationResolver>>()),
            new GraphMessageNormalizer(),
            new GraphMessageToEmlConverter(),
            scopes.Object,
            jobs,
            new NotificationService(Mock.Of<IGenericEntityService>(), Mock.Of<ILogger<NotificationService>>()),
            Mock.Of<ICommunicationEnrichmentService>(),
            Options.Create(new CommunicationOptions { DefaultMailbox = "noreply@contoso.com" }),
            text.Object,
            Options.Create(new AttachmentMatchOptions { Enabled = true }), // the attachment-text match step runs for unverified mail
            new ConfigurationBuilder().Build(),
            new RecordOwnershipResolverDouble(),
            Mock.Of<ILogger<IncomingCommunicationProcessor>>(),
            trackingTokenSigner: signer);
        return (processor, scoped, dv, text);
    }

    public static IEnumerable<object[]> InboundCases()
    {
        yield return ["marked by this server", true];
        yield return ["unmarked", false];
        yield return ["forged marker", false];
        yield return ["marker copied onto other files", false];
        yield return ["marked, but the inbound side has no signer", false];
    }

    /// <summary>
    /// MUTATION (seeded): removing the <c>verifiedServerSend</c> guard from the attachment-text match step, the attachment step or the archive step fails the "marked" case;
    /// honouring a marker without the digest comparison fails the "copied" case; without the signature check, the "forged" case.
    /// </summary>
    [Theory(DisplayName = "151c-1a D2 b SEEDED: a verified server send is no match signal, files no attachment document and archives no .eml; anything else is processed as before")]
    [MemberData(nameof(InboundCases))]
    public async Task Inbound_HonoursOnlyAVerifiedMarker(string kind, bool skipped)
    {
        var server = Server();
        var files = Files();
        var (marker, carried, inboundSigner) = kind switch
        {
            "marked by this server" => (await MarkerFor(server, files), files, (Sprk.Bff.Api.Services.Communication.Tracking.ITrackingTokenSigner?)server),
            "unmarked" => (null, files, server),
            "forged marker" => (await MarkerFor(new FixedKeyTrackingTokenSigner(FixedKeyTrackingTokenSigner.KeyB), files), files, server),
            "marker copied onto other files" => (await MarkerFor(server, files), [("invoice.pdf", Encoding.UTF8.GetBytes("%PDF-1.7 genuine inbound"))], server),
            _ => (await MarkerFor(server, files), files, null),
        };
        var (processor, scoped, dv, text) = Inbound(MessageJson(marker, carried), inboundSigner);

        await processor.ProcessAsync(Mailbox, GraphMessageId, ct: CancellationToken.None);

        dv.Verify(d => d.CreateCommunicationRaceProofAsync(It.IsAny<Entity>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once,
            "the communication row is captured either way");
        var attachmentStepRan = scoped.Requested.Contains(typeof(Sprk.Bff.Api.Services.Dataverse.DocumentIndexParentResolver));
        var archiveStepRan = scoped.Requested.Contains(typeof(Sprk.Bff.Api.Infrastructure.Graph.SpeFileStore));
        attachmentStepRan.Should().Be(!skipped, "attachment documents are filed unless this server sent exactly these files");
        archiveStepRan.Should().Be(!skipped, "the .eml is archived unless this server sent exactly these files");
        text.Verify(t => t.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            skipped ? Times.Never() : Times.AtLeastOnce(),
            "a verified server send's files are not a match signal, so they cannot file the copy under a record named inside them");
    }
}
