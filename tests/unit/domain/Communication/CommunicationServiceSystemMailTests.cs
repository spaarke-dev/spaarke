using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// unified-access-control-r2 task 185 (owner round 98 Q4): the system-mail (secret-bearing) send option, through the REAL
/// <see cref="CommunicationService.SendAsync"/> and the REAL <see cref="EmailChannelSender"/> over a fake Graph transport.
/// With the option, the send is not enriched, the stored <c>sprk_body</c> is the caller's redacted body, and Graph is asked
/// not to save to Sent Items. Without it (the default), all three are unchanged: that half is the regression pin.
/// </summary>
public class CommunicationServiceSystemMailTests
{
    private const string Mailbox = "noreply@contoso.com";
    private const string Secret = "S3cr3tInviteCode_abcdefghijklmnopqrstuvwxyz0";
    private const string SentBody = "<p>Open <a href=\"https://portal.test/?invite=" + Secret + "\">the portal</a></p>";
    private const string StoredBody = "<p>Open <a href=\"[invitation link removed]\">the portal</a></p>";

    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly Mock<ICommunicationEnrichmentService> _enrichment = new();
    private readonly GraphRecorder _graph = new();
    private Entity? _stored;

    public CommunicationServiceSystemMailTests()
    {
        _entities
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) =>
            {
                if (entity.LogicalName == "sprk_communication")
                {
                    _stored = entity;
                }
            })
            .ReturnsAsync(Guid.NewGuid());
    }

    private CommunicationService Sut()
    {
        var options = new CommunicationOptions
        {
            ApprovedSenders = [new ApprovedSenderConfig { Email = Mailbox, DisplayName = "Contoso", IsDefault = true }],
            DefaultMailbox = Mailbox,
        };
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options),
            new CommunicationAccountService(
                Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), Mock.Of<IDistributedCache>(),
                NullLogger<CommunicationAccountService>.Instance),
            Mock.Of<IDistributedCache>(),
            NullLogger<ApprovedSenderValidator>.Instance);
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Returns(new GraphServiceClient(new HttpClient(_graph)));

        return new CommunicationService(
            CommunicationChannelTestFactory.CreateDispatcher(factory.Object),
            senderValidator,
            Mock.Of<ICommunicationDataverseService>(),
            _entities.Object,
            Mock.Of<IDocumentDataverseService>(),
            null!, // CommunicationAccountService — not reached by a shared-mailbox send without attachments
            null!, // JobSubmissionService — not reached
            _enrichment.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            NullLogger<CommunicationService>.Instance);
    }

    private static SendCommunicationRequest Request(bool systemMail) => new()
    {
        To = ["invitee@example.com"],
        Subject = "Your invitation",
        Body = SentBody,
        BodyFormat = BodyFormat.HTML,
        FromMailbox = Mailbox,
        SendMode = SendMode.SharedMailbox,
        CorrelationId = "task-185-system-mail",
        SystemMail = systemMail ? new SystemMailOptions { StoredBody = StoredBody } : null,
    };

    /// <summary>
    /// MUTATIONS: storing the sent body (<c>request.Body</c>) for system mail fails the <c>sprk_body</c> assertion; enriching
    /// it fails the enrichment assertion; keeping <c>SaveToSentItems = true</c> fails the Graph assertion.
    /// </summary>
    [Fact]
    public async Task SystemMail_IsNotEnriched_StoresTheCallersRedactedBody_AndIsNotSavedToSentItems()
    {
        await Sut().SendAsync(Request(systemMail: true));

        _enrichment.Verify(
            e => e.EnrichAsync(It.IsAny<Guid>(), It.IsAny<CommunicationDirection>(), It.IsAny<NormalizedMessage>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never, "a secret-bearing body never reaches AI indexing, triage or proposals");

        _stored.Should().NotBeNull("the communication record is still written");
        _stored!.GetAttributeValue<string>("sprk_body").Should().Be(StoredBody).And.NotContain(Secret);

        var sendMail = _graph.SendMailBody();
        sendMail.Prop("saveToSentItems").GetBoolean().Should().BeFalse("the shared mailbox keeps no copy");
        sendMail.Prop("message").Prop("body").Prop("content").GetString()
            .Should().Contain(Secret, "the RECIPIENT's email carries the link");
        _graph.SentItemsReads.Should().Be(0, "a message not saved to Sent Items is not looked up there");
    }

    /// <summary>Regression pin: a normal send (the default) is enriched, stores its sent body and is saved to Sent Items.</summary>
    [Fact]
    public async Task ANormalSend_IsUnchanged_Enriched_StoresTheSentBody_SavedToSentItems()
    {
        Request(systemMail: false).SystemMail.Should().BeNull("system mail is off by default");

        await Sut().SendAsync(Request(systemMail: false));

        _enrichment.Verify(
            e => e.EnrichAsync(It.IsAny<Guid>(), CommunicationDirection.Outgoing, It.IsAny<NormalizedMessage>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _stored!.GetAttributeValue<string>("sprk_body").Should().Be(SentBody);
        _graph.SendMailBody().Prop("saveToSentItems").GetBoolean().Should().BeTrue();
        _graph.SentItemsReads.Should().Be(1, "the Internet-Message-Id capture still reads Sent Items");
    }

    /// <summary>
    /// The option is defined for the shared-mailbox email path only: on any other path it would be ignored and the secret
    /// stored or archived. Refused before anything is sent or written.
    /// </summary>
    [Theory]
    [InlineData("user-mode")]
    [InlineData("message")]
    [InlineData("archive")]
    public async Task SystemMail_OnAnyOtherPath_IsRefused_BeforeAnythingIsSentOrWritten(string path)
    {
        var request = path switch
        {
            "user-mode" => Request(systemMail: true) with { SendMode = SendMode.User },
            "message" => Request(systemMail: true) with { CommunicationType = CommunicationType.Message },
            _ => Request(systemMail: true) with { ArchiveToSpe = true },
        };

        await FluentActions.Invoking(() => Sut().SendAsync(request)).Should().ThrowAsync<InvalidOperationException>();

        _graph.Requests.Should().BeEmpty();
        _stored.Should().BeNull();
    }

    /// <summary>The option is never JSON-bound: a request body cannot mark its own send as system mail.</summary>
    [Fact]
    public void SystemMail_IsNotBoundFromJson()
    {
        var json = """{"to":["a@example.com"],"subject":"s","body":"b","systemMail":{"storedBody":"x"},"SystemMail":{"StoredBody":"x"}}""";

        var bound = JsonSerializer.Deserialize<SendCommunicationRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        bound.SystemMail.Should().BeNull();
        JsonSerializer.Serialize(Request(systemMail: true), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Should().NotContain("storedBody").And.NotContain(StoredBody);
    }

    /// <summary>A fake Graph transport: records each request; <c>sendMail</c> answers 202, anything else 200 with no messages.</summary>
    private sealed class GraphRecorder : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri, string? Body)> Requests { get; } = new();

        public int SentItemsReads => Requests.Count(r => r.Uri.Contains("sentitems", StringComparison.OrdinalIgnoreCase));

        public JsonElement SendMailBody()
        {
            var send = Requests.Should().ContainSingle(r => r.Uri.EndsWith("/sendMail", StringComparison.OrdinalIgnoreCase)).Subject;
            return JsonDocument.Parse(send.Body!).RootElement;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            if (request.RequestUri.AbsolutePath.EndsWith("/sendMail", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"value":[]}""", System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
