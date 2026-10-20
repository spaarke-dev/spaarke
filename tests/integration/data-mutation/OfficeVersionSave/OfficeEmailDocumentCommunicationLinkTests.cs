using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.Auth.SpeAdmin;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 130 (#2126, UAC D7 T8), end to end through <c>POST /api/office/save</c>: every
/// <c>sprk_document</c> the Outlook add-in creates from an email is CREATED linked to that email's
/// <c>sprk_communication</c> (<c>sprk_relatedcommunication</c>), or — when no communication can be identified — with its
/// own Access Permission Restricted. A Document save gets neither.
/// </summary>
/// <remarks>
/// <para>Before task 130 the link was a separate best-effort write after the create, made only when a Message-ID lookup
/// found a communication: a single attachment was never linked, the capture's own id was discarded, and an unidentified
/// email's document was created open (Standard) with nothing to restrict it.</para>
/// <para>Module-boundary doubles only (ADR-038 §4): the real route, <c>OfficeService</c>, <c>EmailUploadCaptureService</c>
/// and the DI-built <c>OfficeDocumentPersistence</c>, over <see cref="OfficeVersionSaveWorld"/> plus a communication store
/// that is race-proof on the Message-ID, as the <c>sprk_internetmessageid</c> alternate key is. The arrange is a host with
/// several collaborators, inherent at this KEEP integration path (B15).</para>
/// </remarks>
[Trait("status", "task-130-word-add-in-r1")]
public class OfficeEmailDocumentCommunicationLinkTests
{
    private const string RfcId = "<CAF0-130@mail.example.com>";
    private const string ItemId = "AAMkAGI2TG93-130=";
    private const string Subject = "RE: Privileged settlement position";
    private const string Sender = "counsel@test.com";
    private const int Restricted = 100000002;
    private const string PersistenceCategory = "Sprk.Bff.Api.Services.Office.OfficeDocumentPersistence";

    // ── Must fire ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmailSave_IsCreatedLinkedToTheCapturesCommunication_EvenWhenTheMessageIdLookupWouldMiss()
    {
        var store = new CommunicationStore { LookupMisses = true };
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", EmailSave())).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var captured = store.ByMessageId[RfcId];
        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.RelatedCommunicationId.Should().Be(captured, "the capture's own id is authoritative — no re-resolve needed");
        created.AccessPermission.Should().BeNull("a linked document keeps the default Access Permission");
        RelatedCommunicationQueued(world).Should().Be(captured.ToString(), "the worker creates the attachment children linked to it");
    }

    [Fact]
    public async Task EmailSave_WhenTheCaptureProducesNothing_TheHostsPersistenceFindsTheCommunicationByMessageId_AndLinksIt()
    {
        // Also the DI check, behaviourally: only a persistence built with its communication seam (the public constructor
        // the host's AddScoped selects) can find the email's existing communication.
        var store = new CommunicationStore { FailCapture = true };
        var existing = store.Seed(RfcId);
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", EmailSave())).StatusCode.Should().Be(HttpStatusCode.Accepted);

        world.CreatedDocumentRequests.Should().ContainSingle().Which.RelatedCommunicationId.Should().Be(existing);
    }

    [Fact]
    public async Task UnidentifiableEmail_IsCreatedRestricted_LogsAnErrorWithIdsOnly_AndTheSaveSucceeds()
    {
        var store = new CommunicationStore { FailCapture = true }; // nothing captured, and nothing to find
        var world = new OfficeVersionSaveWorld();
        var logs = new RecordingLoggerProvider();
        using var factory = Host(world, store, logs: logs);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", EmailSave());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "an unidentified email is still saved");
        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.AccessPermission.Should().Be(Restricted);
        created.RelatedCommunicationId.Should().BeNull();
        RelatedCommunicationQueued(world).Should().BeNull("its attachment children are created Restricted too");

        var documentId = world.Documents.Keys.Should().ContainSingle().Subject;
        var error = logs.Lines.Should().ContainSingle(l => l.Category == PersistenceCategory && l.Level == LogLevel.Error).Subject;
        error.Message.Should().Contain(documentId.ToString()).And.Contain("Message-ID present: True");
        error.Message.Should().NotContain(Subject).And.NotContain(Sender).And.NotContain(RfcId);
    }

    [Fact]
    public async Task AttachmentSave_WithoutAParentEmail_IsCreatedRestricted()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, new CommunicationStore());

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", AttachmentSave(parentEmailId: null)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.AccessPermission.Should().Be(Restricted);
        created.RelatedCommunicationId.Should().BeNull();
    }

    [Fact]
    public async Task AttachmentSave_ByRfcParentId_IsLinkedToTheCanonicalCommunication_WithoutReadingGraphOrCapturing()
    {
        var store = new CommunicationStore();
        var canonical = store.Seed(RfcId);
        var graph = new ParentEmailGraph();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, graph);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", AttachmentSave(parentEmailId: RfcId)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.RelatedCommunicationId.Should().Be(canonical);
        created.AccessPermission.Should().BeNull("an identified email's attachment is not Restricted");
        graph.RequestedPaths.Should().BeEmpty("Graph cannot address a message by its RFC Message-ID");
        store.Inserts.Should().Be(0);
    }

    [Fact]
    public async Task AttachmentSave_OfAnEmailNotYetCaptured_IsLinkedToTheCommunicationItsParentCaptureCreates()
    {
        var store = new CommunicationStore { LookupMisses = true };
        var graph = new ParentEmailGraph();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, graph);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", AttachmentSave(parentEmailId: ItemId)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        store.Inserts.Should().Be(1, "the parent email is captured, as an email save would capture it");
        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.RelatedCommunicationId.Should().Be(store.ByMessageId[RfcId]);
        created.AccessPermission.Should().BeNull();
        graph.RequestedPaths.Should().ContainSingle().Which.Should().EndWith($"/me/messages/{ItemId}");
    }

    [Fact]
    public async Task EmailThenItsAttachment_ReuseOneCommunication_TheSecondCaptureInsertsNothing()
    {
        var store = new CommunicationStore();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, new ParentEmailGraph());
        var client = factory.CreateClient();

        (await client.PostAsJsonAsync("/api/office/save", EmailSave())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync("/api/office/save", AttachmentSave(parentEmailId: ItemId))).StatusCode.Should().Be(HttpStatusCode.Accepted);

        store.Inserts.Should().Be(1, "the capture is race-proof on the Message-ID: the attachment's parent reconciles");
        world.CreatedDocumentRequests.Should().HaveCount(2)
            .And.OnlyContain(r => r.RelatedCommunicationId == store.ByMessageId[RfcId] && r.AccessPermission == null);
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DocumentSave_GetsNeitherALinkNorRestricted()
    {
        var store = new CommunicationStore();
        store.Seed(RfcId);
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);

        (await factory.CreateClient().PostAsJsonAsync(
                "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Encoding.UTF8.GetBytes("brief"))))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.RelatedCommunicationId.Should().BeNull();
        created.AccessPermission.Should().BeNull();
        RelatedCommunicationQueued(world).Should().BeNull();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static SaveRequest EmailSave() => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
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
        },
    };

    private static SaveRequest AttachmentSave(string? parentEmailId) => new()
    {
        ContentType = SaveContentType.Attachment,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
        Attachment = new AttachmentMetadata
        {
            AttachmentId = $"att-{Guid.NewGuid():N}",
            FileName = "Settlement terms.pdf",
            ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"%PDF-1.7 {Guid.NewGuid()}")),
            ParentEmailId = parentEmailId,
        },
    };

    private static string? RelatedCommunicationQueued(OfficeVersionSaveWorld world) =>
        OfficeVersionSaveWorld.PayloadValue(world.FinalizationPayloads.Should().ContainSingle().Subject, "RelatedCommunicationId");

    /// <summary>
    /// The world's host with the email capture running over <paramref name="store"/> (the same construction
    /// <c>OfficeEmailSaveMessageKeyTests</c> uses), the store as the persistence's communication seam, and — when given —
    /// the parent email Graph returns for an attachment save.
    /// </summary>
    private static WebApplicationFactory<Program> Host(
        OfficeVersionSaveWorld world, CommunicationStore store, ParentEmailGraph? graph = null, RecordingLoggerProvider? logs = null)
    {
        var dataverse = store.Dataverse;
        var ownership = new RecordOwnershipResolverDouble();
        var options = new AutoFileOptions { Enabled = true, Threshold = 0.85 };
        var resolver = new IncomingAssociationResolver(
            new IAssociationRung[] { new ExplicitReferenceRung(dataverse.Object) }, dataverse.Object, dataverse.Object,
            new AssociationStatusMapper(new AutoFileGate(Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options)),
                NullLogger<AssociationStatusMapper>.Instance),
            CoreAncestorResolverFixtures.Inert(), ownership, NullLogger<IncomingAssociationResolver>.Instance);

        return new OfficeVersionSaveTestWebAppFactory(world).WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<EmailUploadCaptureService>();
            services.AddSingleton(new EmailUploadCaptureService(
                dataverse.Object, resolver, Mock.Of<ICommunicationEnrichmentService>(), ownership,
                NullLogger<EmailUploadCaptureService>.Instance));
            services.RemoveAll<ICommunicationDataverseService>();
            services.AddSingleton<ICommunicationDataverseService>(dataverse.Object);

            if (graph is not null)
            {
                services.RemoveAll<OfficeEmailEnricher>();
                services.AddScoped(_ => new OfficeEmailEnricher(graph.Factory, NullLogger<OfficeEmailEnricher>.Instance));
            }

            if (logs is not null)
                services.AddSingleton<ILoggerProvider>(logs);
        }));
    }

    /// <summary>
    /// <c>sprk_communication</c> rows keyed by Message-ID, race-proof as the alternate key makes the real create: a second
    /// create for a known id reconciles to the existing row (<c>WasDuplicate</c>) and inserts nothing.
    /// </summary>
    private sealed class CommunicationStore
    {
        private readonly object _gate = new();

        public CommunicationStore()
        {
            Dataverse
                .Setup(d => d.CreateCommunicationRaceProofAsync(It.IsAny<DataverseEntity>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DataverseEntity _, string? messageId, CancellationToken _) => Create(messageId));
            Dataverse
                .Setup(d => d.GetCommunicationByInternetMessageIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string messageId, CancellationToken _) => Find(messageId));
        }

        public Mock<IDataverseService> Dataverse { get; } = new();

        public Dictionary<string, Guid> ByMessageId { get; } = new(StringComparer.Ordinal);

        public int Inserts { get; private set; }

        /// <summary>The capture's create throws, so the capture is skipped (it is best-effort and returns nothing).</summary>
        public bool FailCapture { get; init; }

        /// <summary>The document create's Message-ID lookup finds nothing, whatever the store holds.</summary>
        public bool LookupMisses { get; init; }

        /// <summary>A communication that already exists for <paramref name="messageId"/> (captured from a mailbox earlier).</summary>
        public Guid Seed(string messageId)
        {
            lock (_gate)
            {
                return ByMessageId[messageId] = Guid.NewGuid();
            }
        }

        private (Guid Id, bool WasDuplicate) Create(string? messageId)
        {
            if (FailCapture)
                throw new InvalidOperationException("Test: Dataverse refused the sprk_communication create.");

            lock (_gate)
            {
                if (messageId is not null && ByMessageId.TryGetValue(messageId, out var existing))
                    return (existing, true);

                Inserts++;
                var id = Guid.NewGuid();
                if (messageId is not null)
                    ByMessageId[messageId] = id;
                return (id, false);
            }
        }

        private DataverseEntity? Find(string messageId)
        {
            lock (_gate)
            {
                return !LookupMisses && ByMessageId.TryGetValue(messageId, out var id) ? new DataverseEntity("sprk_communication", id) : null;
            }
        }
    }

    /// <summary>Graph answering every message read with the attachment's parent email (Message-ID <see cref="RfcId"/>).</summary>
    private sealed class ParentEmailGraph : HttpMessageHandler
    {
        public ParentEmailGraph()
        {
            var client = new GraphServiceClient(new HttpClient(this) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") });
            var factory = new Mock<IGraphClientFactory>();
            factory.Setup(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(client);
            Factory = factory.Object;
        }

        public IGraphClientFactory Factory { get; }

        public List<string> RequestedPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (RequestedPaths)
            {
                RequestedPaths.Add(Uri.UnescapeDataString(request.RequestUri!.AbsolutePath));
            }

            var json = $$"""
            { "id": "{{ItemId}}", "subject": "{{Subject}}", "internetMessageId": "{{RfcId}}", "conversationId": "conv-130",
              "sentDateTime": "2026-10-08T09:30:00Z",
              "from": { "emailAddress": { "address": "{{Sender}}", "name": "Counsel" } },
              "toRecipients": [ { "emailAddress": { "address": "associate@test.com", "name": "Associate" } } ],
              "body": { "contentType": "html", "content": "<p>The email text</p>" }, "hasAttachments": true }
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
