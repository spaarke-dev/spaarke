using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using MimeKit;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 132 (owner UAT 2026-10-22, "the send date error"): a saved email is dated by when it was
/// SENT. The Outlook pane sent the item's last-modified time, and the server discarded the <c>sentDateTime</c> it already
/// read from Graph, so <c>sprk_emaildate</c>, the .eml Date header and the file-name date prefix all carried the day the
/// item was last touched (dev: sent 2026-09-03, stored 2026-10-03).
/// </summary>
/// <remarks>
/// <para>End to end through <c>POST /api/office/save</c> over <see cref="OfficeVersionSaveWorld"/> (module-boundary
/// doubles, ADR-038 §4), so the four places the date lands are read where they are written: the document row's update
/// (<c>sprk_emaildate</c>), the uploaded .eml's bytes, the document name and stored file name, and the finalization
/// payload the worker writes from. Graph is a message handler behind the real Graph SDK.</para>
/// </remarks>
[Trait("status", "task-132-word-add-in-r1")]
public class OfficeEmailSentDateTests
{
    private const string ItemId = "AAMkAGI2TG93-132=";
    private const string RfcId = "<CAF0-132@mail.example.com>";
    private const string Subject = "Fw PAT-942665";

    private static readonly DateTimeOffset SentInTheMailbox = new(2026, 9, 3, 14, 5, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SentByTheClient = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

    // ── End to end ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmailSave_WhenGraphReportsSentDateTime_ItIsTheRowsEmailDate_TheEmlDate_AndTheNamesPrefix()
    {
        var world = new OfficeVersionSaveWorld();
        var graph = new MessageGraph(SentInTheMailbox);
        using var factory = Host(world, graph.Factory);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", EmailSave());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        graph.Reads.Should().Be(1, "the client sent no body, so the mailbox is read");
        ShouldBeDatedEverywhere(world, SentInTheMailbox);
    }

    [Fact]
    public async Task EmailSave_WhenGraphIsNotReached_KeepsTheRequestsSentDate()
    {
        var world = new OfficeVersionSaveWorld();
        var unreachable = new Mock<IGraphClientFactory>();
        unreachable.Setup(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("mailbox not reachable"));
        using var factory = Host(world, unreachable.Object);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", EmailSave());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        ShouldBeDatedEverywhere(world, SentByTheClient);
    }

    // ── The enricher ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Enrich_WhenGraphReportsOnlyTheSentTime_StillReplacesTheRequestsSentDate()
    {
        // Graph returns no body and no attachments: before task 132 the enricher returned the request unchanged here.
        var graph = new MessageGraph(SentInTheMailbox, withBody: false);
        var enricher = new OfficeEmailEnricher(graph.Factory, NullLogger<OfficeEmailEnricher>.Instance);
        var request = EmailSave();

        var result = await enricher.EnrichEmailFromGraphAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.Email!.SentDate.Should().Be(SentInTheMailbox);
        result.Email.Body.Should().BeNull();
    }

    [Fact]
    public async Task Enrich_WhenGraphReportsNoSentTime_KeepsTheRequestsSentDate()
    {
        var graph = new MessageGraph(sentDateTime: null);
        var enricher = new OfficeEmailEnricher(graph.Factory, NullLogger<OfficeEmailEnricher>.Instance);

        var result = await enricher.EnrichEmailFromGraphAsync(EmailSave(), new DefaultHttpContext(), CancellationToken.None);

        result.Email!.SentDate.Should().Be(SentByTheClient);
        result.Email.Body.Should().Contain("The email text", "the body is still enriched");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static void ShouldBeDatedEverywhere(OfficeVersionSaveWorld world, DateTimeOffset expected)
    {
        var prefix = expected.ToString("yyyy-MM-dd") + "_";

        var emailUpdate = world.DocumentUpdates.Should().ContainSingle(u => u.Update.IsEmailArchive == true).Subject.Update;
        emailUpdate.EmailDate.Should().Be(expected.DateTime, "sprk_emaildate");

        world.CreatedDocumentNames.Should().ContainSingle().Which.Should().StartWith(prefix, "sprk_documentname");

        var eml = world.SpeItems.Values.Should().ContainSingle().Subject;
        Path.GetFileName(eml.Name).Should().StartWith(prefix, "the stored file name");
        MimeMessage.Load(new MemoryStream(eml.Versions.Single())).Date.Should().Be(expected, "the .eml Date header");

        var payload = world.FinalizationPayloads.Should().ContainSingle().Subject;
        var emailMetadata = Property(payload, "EmailMetadata");
        Property(emailMetadata, "SentDate").GetDateTimeOffset().Should().Be(expected, "the worker writes from the payload");
    }

    private static JsonElement Property(JsonElement element, string name)
        => element.EnumerateObject().Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>A pane save of a read email: no body (so the server reads the mailbox), the item's dates as Office.js
    /// gives them.</summary>
    private static SaveRequest EmailSave() => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
        Email = new EmailMetadata
        {
            Subject = Subject,
            SenderEmail = "counsel@test.com",
            SentDate = SentByTheClient,
            InternetMessageId = RfcId,
            ExchangeItemId = ItemId,
            IsNameSystemDerived = true,
        },
    };

    private static WebApplicationFactory<Program> Host(OfficeVersionSaveWorld world, IGraphClientFactory graph)
        => new OfficeVersionSaveTestWebAppFactory(world).WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<OfficeEmailEnricher>();
            services.AddScoped(_ => new OfficeEmailEnricher(graph, NullLogger<OfficeEmailEnricher>.Instance));
        }));

    /// <summary>Graph answering every message read with the email, its <c>sentDateTime</c> as given.</summary>
    private sealed class MessageGraph : HttpMessageHandler
    {
        private readonly DateTimeOffset? _sentDateTime;
        private readonly bool _withBody;

        public MessageGraph(DateTimeOffset? sentDateTime, bool withBody = true)
        {
            _sentDateTime = sentDateTime;
            _withBody = withBody;
            var client = new GraphServiceClient(new HttpClient(this) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") });
            var factory = new Mock<IGraphClientFactory>();
            factory.Setup(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(client);
            Factory = factory.Object;
        }

        public IGraphClientFactory Factory { get; }

        public int Reads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Reads++;
            var message = new Dictionary<string, object?>
            {
                ["id"] = ItemId,
                ["subject"] = Subject,
                ["internetMessageId"] = RfcId,
                ["from"] = new { emailAddress = new { address = "counsel@test.com", name = "Counsel" } },
                ["hasAttachments"] = false,
            };
            if (_sentDateTime is { } sent)
                message["sentDateTime"] = sent.ToString("o");
            if (_withBody)
                message["body"] = new { contentType = "html", content = "<p>The email text</p>" };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json"),
            });
        }
    }
}
