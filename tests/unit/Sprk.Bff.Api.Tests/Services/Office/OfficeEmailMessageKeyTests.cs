using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Xrm.Sdk;
using MimeKit;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// spaarkeai-word-add-in-r1 task 121: an email save carries the RFC Message-ID and the Exchange item id in separate
/// fields, and each is used for its own job.
/// </summary>
/// <remarks>
/// <para>Before task 121 the task pane sent the Exchange item id as <c>internetMessageId</c>, so a pane-saved email
/// stored the item id in <c>sprk_emailmessageid</c>, its .eml had no Message-ID header, and it never linked to the
/// communication that captured the same email (task 120 measured this live). Pinned here:</para>
/// <list type="bullet">
/// <item>a request with both keys stores the RFC id, writes it as the .eml's Message-ID, and links the communication
/// found by it;</item>
/// <item>the Graph fallback (no body) fetches by the item id — never by the RFC id, which cannot address a message;</item>
/// <item>a request from an older pane (item id in <c>internetMessageId</c>) is read by the shape of the value: Graph
/// still fetches by it, the .eml gets no Message-ID, and the row stores the item id exactly as before, so task 120's
/// resolve-email-identity still finds it.</item>
/// </list>
/// </remarks>
[Trait("status", "task-121-word-add-in-r1")]
public class OfficeEmailMessageKeyTests
{
    private const string RfcId = "<CAF0a1b2c3@mail.example.com>";
    private const string ItemId = "AAMkAGI2TG93AAA=";

    private static EmailMetadata Email(string? internetMessageId, string? exchangeItemId, string? body = null) => new()
    {
        Subject = "RE: Discovery schedule",
        SenderEmail = "counsel@contoso.com",
        InternetMessageId = internetMessageId,
        ExchangeItemId = exchangeItemId,
        Body = body,
        IsBodyHtml = true,
    };

    private static SaveRequest EmailSave(EmailMetadata email) => new() { ContentType = SaveContentType.Email, Email = email };

    // ── Which value is which ───────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(RfcId, ItemId, RfcId)] // this pane / Quick Save with the item id
    [InlineData(RfcId, null, RfcId)] // Quick Save before task 121
    [InlineData(ItemId, null, ItemId)] // a pane older than task 121: the item id arrived as internetMessageId
    [InlineData("AQMkADAwATM0MDAAMS1iNWU3LTA0YTEtMDACLTAwCgBGAAAD", null, "AQMkADAwATM0MDAAMS1iNWU3LTA0YTEtMDACLTAwCgBGAAAD")]
    [InlineData(null, ItemId, ItemId)] // a draft: no Message-ID until it is sent
    [InlineData("no-at-sign-message-id", ItemId, "no-at-sign-message-id")] // a task-121 client's value is taken as sent
    [InlineData(null, null, null)]
    public void ResolveStoredMessageId_PrefersTheRfcMessageId_ElseKeepsTheItemIdAsBefore(
        string? internetMessageId, string? exchangeItemId, string? expected)
    {
        OfficeEmailEnricher.ResolveStoredMessageId(Email(internetMessageId, exchangeItemId)).Should().Be(expected);
    }

    [Theory]
    [InlineData(RfcId, ItemId, ItemId)]
    [InlineData(ItemId, null, ItemId)] // older pane
    [InlineData("AAMk-with-an-@-sign", null, "AAMk-with-an-@-sign")] // the item-id prefix wins over a stray '@'
    [InlineData(RfcId, null, null)] // an RFC id is never treated as an item id
    public void ResolveExchangeItemId_ReadsTheNewField_OrAnOlderPanesItemId_NeverAnRfcId(
        string? internetMessageId, string? exchangeItemId, string? expected)
    {
        OfficeEmailEnricher.ResolveExchangeItemId(Email(internetMessageId, exchangeItemId)).Should().Be(expected);
    }

    // ── The .eml ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildEml_PaneRequestWithBothKeys_WritesTheMessageIdHeader_AndNoItemIdHeader()
    {
        using var eml = OfficeEmailEnricher.BuildEmlFromMetadata(Email(RfcId, ItemId, "<p>text</p>"));

        var message = MimeMessage.Load(eml);
        message.MessageId.Should().Be("CAF0a1b2c3@mail.example.com");
        message.Headers.Contains("X-Exchange-Item-Id").Should().BeFalse("the mailbox-specific id is not part of the archived message");
    }

    [Theory]
    [InlineData(ItemId)]
    [InlineData("AQMkADAwATM0MDAAMS1iNWU3LTA0YTEtMDACLTAwCgBGAAAD")]
    public void BuildEml_OlderPaneSentTheItemIdAsInternetMessageId_WritesNoMessageId_KeepsTheItemIdHeader(string itemId)
    {
        using var eml = OfficeEmailEnricher.BuildEmlFromMetadata(Email(itemId, exchangeItemId: null, "<p>text</p>"));

        var message = MimeMessage.Load(eml);
        message.Headers["X-Exchange-Item-Id"].Should().Be(itemId);
        message.MessageId.Should().NotBe(itemId, "an Exchange item id is never written as the Message-ID");
    }

    // ── The Graph fallback (the client sent no body) ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichEmailFromGraph_NoBody_FetchesByTheExchangeItemId_NotByTheMessageId()
    {
        var (enricher, handler) = EnricherWithGraph();

        var result = await enricher.EnrichEmailFromGraphAsync(
            EmailSave(Email(RfcId, ItemId)), new DefaultHttpContext(), CancellationToken.None);

        handler.RequestedPaths.Should().ContainSingle()
            .Which.Should().EndWith($"/me/messages/{ItemId}");
        result.Email!.Body.Should().Be("<p>From Graph</p>");
        result.Email.InternetMessageId.Should().Be(RfcId, "the fallback fills in content, never the keys");
    }

    [Fact]
    public async Task EnrichEmailFromGraph_OlderPaneSentTheItemIdAsInternetMessageId_StillFetchesByIt()
    {
        var (enricher, handler) = EnricherWithGraph();

        var result = await enricher.EnrichEmailFromGraphAsync(
            EmailSave(Email(ItemId, exchangeItemId: null)), new DefaultHttpContext(), CancellationToken.None);

        handler.RequestedPaths.Should().ContainSingle().Which.Should().EndWith($"/me/messages/{ItemId}");
        result.Email!.Body.Should().Be("<p>From Graph</p>");
    }

    [Fact]
    public async Task EnrichEmailFromGraph_OnlyAnRfcMessageId_NeverCallsGraph()
    {
        // Graph addresses a message by its item id; an RFC Message-ID alone cannot be fetched, so it is not tried.
        var graph = new Mock<IGraphClientFactory>(MockBehavior.Strict);
        var enricher = new OfficeEmailEnricher(graph.Object, NullLogger<OfficeEmailEnricher>.Instance);
        var request = EmailSave(Email(RfcId, exchangeItemId: null));

        var result = await enricher.EnrichEmailFromGraphAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeSameAs(request);
    }

    // ── The stored row and the communication link ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDocumentWithSpePointers_PaneEmailSave_StoresTheRfcMessageId_AndLinksTheCommunicationFoundByIt()
    {
        var newDocId = Guid.NewGuid();
        var communicationId = Guid.NewGuid();
        var (docSvc, captured) = DocumentServiceCapturingTheUpdate(newDocId);

        var comm = new Mock<ICommunicationDataverseService>(MockBehavior.Strict);
        comm.Setup(c => c.GetCommunicationByInternetMessageIdAsync(RfcId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", communicationId));

        var generic = new Mock<IGenericEntityService>();
        generic.Setup(g => g.RetrieveAsync("sprk_document", newDocId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", newDocId));
        Dictionary<string, object>? link = null;
        generic.Setup(g => g.UpdateAsync("sprk_document", newDocId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => link = f)
            .Returns(Task.CompletedTask);

        var sut = new OfficeDocumentPersistence(
            docSvc.Object, NotADuplicate().Object, NullLogger<OfficeDocumentPersistence>.Instance, comm.Object, generic.Object);

        await sut.CreateDocumentWithSpePointersAsync(
            EmailSave(Email(RfcId, ItemId)), "drive1", "item2", "https://spe/web", "email.eml", 100, "user-oid",
            CancellationToken.None, owningTeamId: RecordOwnershipResolverDouble.DefaultTeamId);

        captured().EmailMessageId.Should().Be(RfcId);
        link.Should().NotBeNull("the archive links to the communication that captured the same email (FR-C4)");
        ((EntityReference)link![CrossPathLink.LinkedCommunicationAttribute]).Id.Should().Be(communicationId);
    }

    [Fact]
    public async Task CreateDocumentWithSpePointers_OlderPaneSentTheItemId_StoresTheItemIdAsBefore()
    {
        var newDocId = Guid.NewGuid();
        var (docSvc, captured) = DocumentServiceCapturingTheUpdate(newDocId);
        var sut = new OfficeDocumentPersistence(
            docSvc.Object, NotADuplicate().Object, NullLogger<OfficeDocumentPersistence>.Instance);

        await sut.CreateDocumentWithSpePointersAsync(
            EmailSave(Email(ItemId, exchangeItemId: null)), "drive1", "item2", "https://spe/web", "email.eml", 100,
            "user-oid", CancellationToken.None, owningTeamId: RecordOwnershipResolverDouble.DefaultTeamId);

        captured().EmailMessageId.Should().Be(ItemId, "resolve-email-identity finds such a save by its item id (task 120)");
    }

    // ── The wire ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheJsonTheAddInSends_BindsBothKeys_AndAnOlderBodyWithoutTheNewFieldStillBinds()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        const string current = $$"""
        { "contentType": "Email",
          "email": { "subject": "s", "senderEmail": "a@contoso.com", "internetMessageId": "{{RfcId}}", "exchangeItemId": "{{ItemId}}" } }
        """;
        const string older = $$"""
        { "contentType": "Email",
          "email": { "subject": "s", "senderEmail": "a@contoso.com", "internetMessageId": "{{ItemId}}" } }
        """;

        var now = JsonSerializer.Deserialize<SaveRequest>(current, options)!.Email!;
        var before = JsonSerializer.Deserialize<SaveRequest>(older, options)!.Email!;

        now.InternetMessageId.Should().Be(RfcId);
        now.ExchangeItemId.Should().Be(ItemId);
        before.ExchangeItemId.Should().BeNull();
        OfficeEmailEnricher.ResolveExchangeItemId(before).Should().Be(ItemId);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Mock<ContentDedupDetector> NotADuplicate()
    {
        var mock = new Mock<ContentDedupDetector>(MockBehavior.Loose, null!, null!, null!, NullLogger<ContentDedupDetector>.Instance);
        mock.Setup(d => d.ReconcileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DedupDecision("h", IsDuplicate: false, CanonicalDocumentId: null));
        return mock;
    }

    private static (Mock<IDocumentDataverseService> Service, Func<UpdateDocumentRequest> Captured) DocumentServiceCapturingTheUpdate(Guid newDocId)
    {
        UpdateDocumentRequest? update = null;
        var docSvc = new Mock<IDocumentDataverseService>();
        docSvc.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newDocId.ToString());
        docSvc.Setup(d => d.UpdateDocumentAsync(newDocId.ToString(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, UpdateDocumentRequest, CancellationToken>((_, u, _) => update = u)
            .Returns(Task.CompletedTask);
        return (docSvc, () => update ?? throw new InvalidOperationException("the document row was never updated"));
    }

    private static (OfficeEmailEnricher Enricher, RecordingGraphHandler Handler) EnricherWithGraph()
    {
        var handler = new RecordingGraphHandler();
        var client = new GraphServiceClient(new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") });
        var graph = new Mock<IGraphClientFactory>();
        graph.Setup(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(client);
        return (new OfficeEmailEnricher(graph.Object, NullLogger<OfficeEmailEnricher>.Instance), handler);
    }

    /// <summary>Answers every Graph request with one message and records the path it was asked for.</summary>
    private sealed class RecordingGraphHandler : HttpMessageHandler
    {
        public List<string> RequestedPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedPaths.Add(Uri.UnescapeDataString(request.RequestUri!.AbsolutePath));
            const string json = """
            { "id": "graph-id", "body": { "contentType": "html", "content": "<p>From Graph</p>" }, "hasAttachments": false }
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
