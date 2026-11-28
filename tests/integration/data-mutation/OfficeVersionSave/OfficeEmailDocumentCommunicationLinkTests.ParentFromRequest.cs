using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Graph;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 143: an attachment saved on its own (the pane's "save the attachments without the email")
/// whose parent the server cannot read from Graph — its id is an RFC Message-ID, or the mailbox is a guest's — takes its
/// parent from the request's own <c>email</c>, when that email IS the parent (its keys equal
/// <see cref="AttachmentMetadata.ParentEmailId"/>). The parent is then captured and filed exactly as when Graph read it
/// (task 130), so the attachment's document is linked to its email's communication instead of created Restricted.
/// </summary>
/// <remarks>The same host and doubles as the rest of this class (task 130).</remarks>
public partial class OfficeEmailDocumentCommunicationLinkTests
{
    // ── Must fire ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("status", "task-143-word-add-in-r1")]
    public async Task AttachmentSave_ByRfcParent_CarryingItsEmail_CapturesTheParentFromTheRequest_AndIsLinkedToIt()
    {
        var store = new CommunicationStore();
        var graph = new ParentEmailGraph();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, graph);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", AttachmentWithItsEmail(parentEmailId: RfcId)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        store.Inserts.Should().Be(1, "the parent email is captured from the request, as an email save would capture it");
        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.RelatedCommunicationId.Should().Be(store.ByMessageId[RfcId]);
        created.AccessPermission.Should().BeNull("the parent was identified, so the document is not Restricted");
        graph.RequestedPaths.Should().BeEmpty("no Graph read: the bytes and the parent both came from the pane");
    }

    [Fact]
    [Trait("status", "task-143-word-add-in-r1")]
    public async Task AttachmentSave_FromAMailboxGraphCannotRead_CarryingItsEmail_CapturesTheParentFromTheRequest()
    {
        // A guest's mailbox: the parent's item id is not in the caller's own mailbox, so Graph answers 404.
        var store = new CommunicationStore();
        var graph = new UnreadableMailboxGraph();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, new ParentEmailGraph(), graphFactory: graph.Factory);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", AttachmentWithItsEmail(parentEmailId: ItemId)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        graph.Reads.Should().Be(1, "the parent is still asked of Graph first");
        store.Inserts.Should().Be(1);
        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.RelatedCommunicationId.Should().Be(store.ByMessageId[RfcId]);
        created.AccessPermission.Should().BeNull();
    }

    [Fact]
    [Trait("status", "task-143-word-add-in-r1")]
    public async Task AttachmentSave_CarryingItsUnfiledCapturedEmail_FilesThatEmailToTheRecord_AskingWithTheCallersToken()
    {
        var store = new CommunicationStore();
        var communicationId = store.Seed(RfcId);
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, new ParentEmailGraph());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);
        var request = AttachmentWithItsEmail(parentEmailId: RfcId);

        (await client.PostAsJsonAsync("/api/office/save", request)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        store.CommunicationWrites.Should().ContainSingle().Which.Id.Should().Be(communicationId,
            "saving one attachment files its parent email as saving the email would (owner decision 2026-10-20)");
        store.Probe.Verify(
            p => p.GetCallerRightsAsync(CallerToken, "sprk_communications", communicationId, It.IsAny<CancellationToken>()),
            Times.Once);
        world.CreatedDocumentRequests.Should().ContainSingle().Which.RelatedCommunicationId.Should().Be(communicationId);
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("status", "task-143-word-add-in-r1")]
    public async Task AttachmentSave_CarryingAnotherEmail_NeverCapturesIt_AndIsRestricted()
    {
        var store = new CommunicationStore();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, new ParentEmailGraph());
        var request = AttachmentWithItsEmail(parentEmailId: RfcId) is var r
            ? r with { Email = r.Email! with { InternetMessageId = "<another-email-143@mail.example.com>", ExchangeItemId = null } }
            : null!;

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", request)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        store.Inserts.Should().Be(0, "an email that is not the attachment's parent is never captured");
        store.CaptureCreates.Should().BeEmpty();
        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.AccessPermission.Should().Be(Restricted);
        created.RelatedCommunicationId.Should().BeNull();
    }

    [Fact]
    [Trait("status", "task-143-word-add-in-r1")]
    public async Task AttachmentSave_ToNoRecord_CarryingItsEmail_CapturesItUnfiled_AndFilesNothing()
    {
        var store = new CommunicationStore();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, new ParentEmailGraph());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);
        var request = AttachmentWithItsEmail(parentEmailId: RfcId) with { TargetEntity = null };

        (await client.PostAsJsonAsync("/api/office/save", request)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        store.Inserts.Should().Be(1);
        // The new capture's own association-status write may run; it never names a record.
        store.CommunicationWrites.SelectMany(w => w.Fields.Values).OfType<Microsoft.Xrm.Sdk.EntityReference>()
            .Should().BeEmpty("there is no record to file the email to");
        store.Probe.Verify(
            p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "no filing is attempted");
        world.CreatedDocumentRequests.Should().ContainSingle().Which.RelatedCommunicationId.Should().Be(store.ByMessageId[RfcId]);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One ticked attachment as the pane sends it: its bytes, its parent's id, and the open email itself.</summary>
    private static SaveRequest AttachmentWithItsEmail(string parentEmailId) => AttachmentSave(parentEmailId) with
    {
        Email = new EmailMetadata
        {
            Subject = Subject,
            SenderEmail = Sender,
            SentDate = new DateTimeOffset(2026, 10, 8, 9, 30, 0, TimeSpan.Zero),
            InternetMessageId = RfcId,
            ExchangeItemId = ItemId,
            Body = "<p>The email text</p>",
            IsBodyHtml = true,
            IsNameSystemDerived = true,
            Attachments = new List<AttachmentReference>
            {
                new() { AttachmentId = "att-143", FileName = "Settlement terms.pdf", ContentType = "application/pdf", Size = 2048 },
            },
        },
    };

    /// <summary>Graph for a mailbox the caller's token cannot read: every message read is a 404.</summary>
    private sealed class UnreadableMailboxGraph : HttpMessageHandler
    {
        public UnreadableMailboxGraph()
        {
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
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    """{ "error": { "code": "ErrorItemNotFound", "message": "The specified object was not found in the store." } }""",
                    Encoding.UTF8, "application/json"),
            });
        }
    }
}
