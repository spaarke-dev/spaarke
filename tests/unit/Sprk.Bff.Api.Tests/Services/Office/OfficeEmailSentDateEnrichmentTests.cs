using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// spaarkeai-word-add-in-r1 task 132, review F4: a saved email is dated by when it was SENT even though the client sends
/// the body. Since task 116a the pane and Quick Save always send the body, so the full Graph fetch (which already selects
/// <c>sentDateTime</c>) never ran and the stored date was the client's <c>dateTimeCreated</c> — the draft's creation time
/// for a Sent Items message. A request that carries a body and an Exchange item id now makes ONE light Graph read of
/// <c>sentDateTime</c> alone.
/// </summary>
/// <remarks>
/// The end-to-end counterpart (the date landing in <c>sprk_emaildate</c>, the .eml Date header and the file-name prefix)
/// is <c>OfficeEmailSentDateTests</c> in the data-mutation folder.
/// </remarks>
[Trait("status", "task-132-word-add-in-r1")]
public class OfficeEmailSentDateEnrichmentTests
{
    private const string RfcId = "<CAF0a1b2c3-132@mail.example.com>";
    private const string ItemId = "AAMkAGI2TG93-132-secret-item-id=";

    private static readonly DateTimeOffset SentInTheMailbox = new(2026, 9, 3, 14, 5, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SentByTheClient = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

    private static SaveRequest EmailSave(string? exchangeItemId = ItemId, string? body = "<p>The email text</p>") => new()
    {
        ContentType = SaveContentType.Email,
        Email = new EmailMetadata
        {
            Subject = "Fw PAT-942665",
            SenderEmail = "counsel@contoso.com",
            InternetMessageId = RfcId,
            ExchangeItemId = exchangeItemId,
            SentDate = SentByTheClient,
            Body = body,
            IsBodyHtml = true,
        },
    };

    [Fact]
    public async Task BodyAndItemId_GraphsSentDateTimeReplacesTheClientsValue_WithOneLightRead()
    {
        var graph = new GraphDouble(_ => Json(sentDateTime: SentInTheMailbox));
        var request = EmailSave();

        var result = await graph.Enricher.EnrichEmailFromGraphAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.Email!.SentDate.Should().Be(SentInTheMailbox);
        result.Email.Body.Should().Be("<p>The email text</p>", "the client's content is untouched");
        graph.Requests.Should().ContainSingle();
        graph.Requests[0].Path.Should().EndWith($"/me/messages/{ItemId}");
        graph.Requests[0].Query.Should().Contain("select=sentDateTime", "only the sent time is read")
            .And.NotContain("expand", "no attachments are fetched")
            .And.NotContain("body");
    }

    [Fact]
    public async Task BodyAndItemId_WhenGraphReportsNoSentTime_KeepsTheClientsValue_AndTheSameRequest()
    {
        var graph = new GraphDouble(_ => Json(sentDateTime: null));
        var request = EmailSave();

        var result = await graph.Enricher.EnrichEmailFromGraphAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeSameAs(request);
        graph.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task BodyAndItemId_WhenGraphCannotBeReached_KeepsTheClientsValue_AndLogsNoMessageId()
    {
        // A B2B guest's mailbox is in their home tenant: the Graph client cannot even be built for them.
        var logger = new CapturingLogger();
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"mailbox {ItemId} not reachable"));
        var enricher = new OfficeEmailEnricher(factory.Object, logger);
        var request = EmailSave();

        var result = await enricher.EnrichEmailFromGraphAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeSameAs(request, "a failed light read never fails or changes the save");
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
        AssertNoIdentifiers(logger);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task BodyAndItemId_WhenGraphAnswersAnError_KeepsTheClientsValue_AndLogsNoMessageId(HttpStatusCode status)
    {
        var logger = new CapturingLogger();
        var graph = new GraphDouble(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(
                $$"""{ "error": { "code": "ErrorItemNotFound", "message": "The message {{ItemId}} was not found." } }""",
                Encoding.UTF8, "application/json"),
        });
        var enricher = new OfficeEmailEnricher(graph.Factory, logger);
        var request = EmailSave();

        var result = await enricher.EnrichEmailFromGraphAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeSameAs(request);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
        AssertNoIdentifiers(logger);
    }

    [Fact]
    public async Task BodyAndItemId_WhenTheReadTimesOutOutsideTheCaller_KeepsTheClientsValue()
    {
        var graph = new GraphDouble(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));
        var request = EmailSave();

        var result = await graph.Enricher.EnrichEmailFromGraphAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeSameAs(request);
    }

    [Fact]
    public async Task BodyAndItemId_WhenTheCallerCancels_TheCancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        var graph = new GraphDouble(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        var act = () => graph.Enricher.EnrichEmailFromGraphAsync(EmailSave(), new DefaultHttpContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task BodyButNoItemId_NeverCallsGraph_AndKeepsTheClientsValue()
    {
        // Only the RFC Message-ID: Graph cannot address a message by it, so the mailbox is not read at all.
        var graph = new Mock<IGraphClientFactory>(MockBehavior.Strict);
        var enricher = new OfficeEmailEnricher(graph.Object, new CapturingLogger());
        var request = EmailSave(exchangeItemId: null);

        var result = await enricher.EnrichEmailFromGraphAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeSameAs(request);
        graph.Verify(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoBodyAndItemId_StillTakesTheFullFetch_WhichSelectsTheSentTimeToo()
    {
        var graph = new GraphDouble(_ => Json(sentDateTime: SentInTheMailbox, body: "<p>From Graph</p>"));

        var result = await graph.Enricher.EnrichEmailFromGraphAsync(
            EmailSave(body: null), new DefaultHttpContext(), CancellationToken.None);

        result.Email!.SentDate.Should().Be(SentInTheMailbox);
        result.Email.Body.Should().Be("<p>From Graph</p>");
        graph.Requests.Should().ContainSingle("the full fetch is one read, not two");
        graph.Requests[0].Query.Should().Contain("sentDateTime").And.Contain("expand=attachments");
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static void AssertNoIdentifiers(CapturingLogger logger)
    {
        foreach (var entry in logger.Entries)
        {
            entry.Text.Should().NotContain(ItemId).And.NotContain("secret-item-id").And.NotContain(RfcId);
            entry.Exception.Should().BeNull("the exception can carry the request URL, which holds the message id");
        }
    }

    private static HttpResponseMessage Json(DateTimeOffset? sentDateTime, string? body = null)
    {
        var parts = new List<string> { "\"id\": \"graph-id\"", "\"hasAttachments\": false" };
        if (sentDateTime is { } sent)
            parts.Add($"\"sentDateTime\": \"{sent:o}\"");
        if (body is not null)
            parts.Add($"\"body\": {{ \"contentType\": \"html\", \"content\": \"{body}\" }}");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ " + string.Join(", ", parts) + " }", Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>The real Graph SDK over a message handler that answers as given and records what it was asked.</summary>
    private sealed class GraphDouble : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;

        public GraphDouble(Func<HttpRequestMessage, HttpResponseMessage> answer)
        {
            _answer = answer;
            var client = new GraphServiceClient(new HttpClient(this) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") });
            var factory = new Mock<IGraphClientFactory>();
            factory.Setup(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(client);
            Factory = factory.Object;
            Enricher = new OfficeEmailEnricher(Factory, new CapturingLogger());
        }

        public IGraphClientFactory Factory { get; }

        public OfficeEmailEnricher Enricher { get; }

        public List<(string Path, string Query)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // $select / $expand keep their '$' on the wire; compare without it so the assertion reads naturally.
            Requests.Add((Uri.UnescapeDataString(request.RequestUri!.AbsolutePath), Uri.UnescapeDataString(request.RequestUri.Query).Replace("$", "")));
            return Task.FromResult(_answer(request));
        }
    }

    private sealed class CapturingLogger : ILogger<OfficeEmailEnricher>
    {
        public List<(LogLevel Level, string Text, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
