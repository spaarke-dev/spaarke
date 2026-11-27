using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 141 review: the add that NAMES a saved <c>.eml</c> (<c>email.savedDocumentId</c>) is tied to
/// the email that <c>.eml</c> stores, and to its record's storage. Findings 3 and 4 of the review, plus the container nit.
/// </summary>
/// <remarks>
/// <para>Finding 4: the request's RFC Message-ID was never compared with the named <c>.eml</c>, and the add still ran the email
/// capture and the filing step and linked the children to a communication, all from the REQUEST. Now the planner refuses a
/// request that describes another email (403 <c>OFFICE_009</c>), the add runs no capture or filing, and the children take the
/// <c>.eml</c> row's own <c>sprk_relatedcommunication</c>.</para>
/// <para>Finding 3: an attachment the stored <c>.eml</c> holds but no save creates (the noise filter drops a calendar file) was
/// answered "nothing to add", which the pane showed as saved. It is now reported in <c>attachmentsNotSaveable</c>.</para>
/// <para>The same world as <see cref="OfficeEmailAttachmentsAddByDocumentTests"/> (module-boundary doubles, ADR-038 §4); the
/// capture test also runs the REAL capture over a communication double, as <c>OfficeSaveConfirmsSuggestedFilingTests</c> does.</para>
/// </remarks>
[Trait("status", "task-141-word-add-in-r1")]
public class OfficeEmailAttachmentsAddReviewTests
{
    private const string RfcId = "<CAF0-141-review@mail.example.com>";
    private const string OtherRfcId = "<CAF0-141-other-email@mail.example.com>";
    private const string A = "Settlement terms.pdf";
    private const string B = "Exhibit A.pdf";
    private const string Invite = "Meeting.ics";

    private static readonly SaveEntityReference Matter = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("14114114-0141-4141-8141-0000000000ef"),
    };

    [Fact]
    public async Task Add_WhoseRequestDescribesAnotherEmail_Is403_AndWritesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        var jobsBefore = world.Jobs.Count;
        var updatesBefore = world.DocumentUpdates.Count + world.GenericUpdates.Count;

        var response = await client.SendAsync(Add(emlId, new[] { B }, "add-other-email", messageId: OtherRfcId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the named .eml does not store the email being saved");
        (await Body(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        world.FinalizationPayloads.Should().ContainSingle("no child is queued under the other email's .eml");
        world.Jobs.Should().HaveCount(jobsBefore, "refused before the job row");
        world.DocumentCreates.Should().Be(1);
        (world.DocumentUpdates.Count + world.GenericUpdates.Count).Should().Be(updatesBefore, "nothing is written");
    }

    [Fact]
    public async Task Add_ToAMailboxCapturedEml_MatchesThroughItsOwnCommunication_AndRefusesAnotherEmail()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        // A mailbox-captured archive stores no message id of its own; its communication carries the RFC Message-ID.
        var communicationId = Guid.NewGuid();
        world.Documents[emlId].EmailMessageId = null;
        world.Documents[emlId].RelatedCommunicationId = communicationId;
        world.CommunicationsByMessageId[RfcId] = communicationId;

        var other = await client.SendAsync(Add(emlId, new[] { B }, "add-captured-other", messageId: OtherRfcId));
        var same = await client.SendAsync(Add(emlId, new[] { B }, "add-captured-same"));

        other.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        same.StatusCode.Should().Be(HttpStatusCode.Accepted, "the identity route found this .eml through the same link");
        OfficeVersionSaveWorld.PayloadValue(world.FinalizationPayloads.Last(), "RelatedCommunicationId")
            .Should().Be(communicationId.ToString(), "the children take the .eml row's own communication");
    }

    [Fact]
    public async Task Add_LinksTheChildrenToTheEmlRowsOwnCommunication_AndRunsNoCapture()
    {
        var world = new OfficeVersionSaveWorld();
        var captured = new List<Guid>();
        var rowCommunication = Guid.NewGuid();
        var laterCommunication = Guid.NewGuid();
        using var factory = WithRealCapture(world, captured, rowCommunication, laterCommunication);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        world.Documents[emlId].RelatedCommunicationId.Should().Be(rowCommunication, "precondition: the first save linked it");
        captured.Should().ContainSingle("precondition: the harness sees the first save's capture");

        var response = await client.SendAsync(Add(emlId, new[] { B }, "add-own-communication"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        OfficeVersionSaveWorld.PayloadValue(world.FinalizationPayloads.Last(), "RelatedCommunicationId")
            .Should().Be(rowCommunication.ToString(), "never a communication captured or looked up from the request");
        captured.Should().ContainSingle("an add captures and files no email");
        (await Body(response)).TryGetProperty("emailFiling", out _).Should().BeFalse("no filing step ran");
    }

    [Fact]
    public async Task Add_WhenTheRecordsContainerIsNotTheEmls_Is403_AndWritesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        var jobsBefore = world.Jobs.Count;
        world.Documents[emlId].DriveId.Should().NotBe("drive-secure-141", "precondition");
        // The record became secure: its documents are now kept in its own container, not where the .eml is.
        world.SecureRecords[("sprk_matter", Matter.EntityId)] = "drive-secure-141";

        var response = await client.SendAsync(Add(emlId, new[] { B }, "add-other-container"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        world.FinalizationPayloads.Should().ContainSingle("no child is queued in another container");
        world.Jobs.Should().HaveCount(jobsBefore);
        world.DocumentCreates.Should().Be(1);
    }

    [Fact]
    public async Task Add_NamingAnAttachmentNoSaveCreates_ReportsItNotSaveable_NeverNothingToAdd()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        var jobsBefore = world.Jobs.Count;

        var only = await client.SendAsync(Add(emlId, new[] { Invite }, "add-invite"));

        only.StatusCode.Should().Be(HttpStatusCode.OK, "nothing can be added, so no job");
        (await Names(only, "attachmentsNotSaveable")).Should().Equal(Invite);
        (await Body(only)).TryGetProperty("attachmentsNotStored", out _).Should().BeFalse("the .eml does hold it");
        world.Jobs.Should().HaveCount(jobsBefore);

        var mixed = await client.SendAsync(Add(emlId, new[] { B, Invite }, "add-b-invite"));

        mixed.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Names(mixed, "attachmentsAdded")).Should().Equal(B);
        (await Names(mixed, "attachmentsNotSaveable")).Should().Equal(Invite);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The first save, with only <paramref name="saved"/> ticked, under its own body key, then its children.</summary>
    private static async Task<Guid> FirstSaveWithChildren(HttpClient client, OfficeVersionSaveWorld world, params string[] saved)
    {
        var first = EmailSave(saved.ToList(), RfcId) with { IdempotencyKey = "first-save-review" };
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save") { Content = JsonContent.Create(first) };
        message.Headers.Add("X-Idempotency-Key", "first-save-review");
        (await client.SendAsync(message)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var emlId = world.Documents.Values.Should()
            .ContainSingle(d => d.FileName != null && d.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Subject.Id;
        world.Documents[emlId].EmailMessageId.Should().Be(RfcId, "precondition: the save stored the email's Message-ID");
        foreach (var name in saved)
            world.SeedAttachmentChild(emlId, name);
        return emlId;
    }

    private static SaveRequest EmailSave(List<string>? selection, string messageId) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = Matter,
        Email = new EmailMetadata
        {
            Subject = "RE: Settlement",
            SenderEmail = "counsel@test.com",
            SentDate = new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero),
            InternetMessageId = messageId,
            Body = "<p>Attached.</p>",
            IsBodyHtml = true,
            IsNameSystemDerived = true,
            Attachments = new List<AttachmentReference>
            {
                Attachment(A, "application/pdf"), Attachment(B, "application/pdf"), Attachment(Invite, "text/calendar"),
            },
            SelectedAttachmentFileNames = selection,
        },
    };

    /// <summary>The pane's add, naming the saved <c>.eml</c>; <paramref name="messageId"/> is the email the request describes.</summary>
    private static HttpRequestMessage Add(Guid savedDocumentId, string[] names, string headerKey, string messageId = RfcId)
    {
        var json = JsonSerializer.SerializeToNode(EmailSave(names.ToList(), messageId))!.AsObject();
        json["Email"]!.AsObject()["savedDocumentId"] = savedDocumentId.ToString("D");
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save")
        {
            Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("X-Idempotency-Key", headerKey);
        return message;
    }

    private static AttachmentReference Attachment(string name, string contentType) => new()
    {
        AttachmentId = $"att-{name}",
        FileName = name,
        ContentType = contentType,
        Size = 20_000,
        ContentBase64 = Convert.ToBase64String(new byte[20_000]),
    };

    /// <summary>
    /// The world's host with the REAL email capture over a communication double: each capture creates a communication, the
    /// first <paramref name="first"/>, every later one <paramref name="later"/>, recorded in <paramref name="captured"/>. The
    /// Message-ID lookup also answers <paramref name="later"/>, so a child linked from the request would show it.
    /// </summary>
    private static WebApplicationFactory<Program> WithRealCapture(
        OfficeVersionSaveWorld world, List<Guid> captured, Guid first, Guid later)
    {
        var communications = new Mock<IDataverseService>();
        communications
            .Setup(d => d.CreateCommunicationRaceProofAsync(It.IsAny<DataverseEntity>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                lock (captured)
                {
                    var id = captured.Count == 0 ? first : later;
                    captured.Add(id);
                    return (id, false);
                }
            });
        communications
            .Setup(d => d.GetCommunicationByInternetMessageIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_communication", later));

        var ownership = new RecordOwnershipResolverDouble();
        var options = new AutoFileOptions { Enabled = true, Threshold = 0.85 };
        var resolver = new IncomingAssociationResolver(
            new IAssociationRung[] { new ExplicitReferenceRung(communications.Object) }, communications.Object, communications.Object,
            new AssociationStatusMapper(new AutoFileGate(Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options)),
                NullLogger<AssociationStatusMapper>.Instance),
            CoreAncestorResolverFixtures.Inert(), ownership, NullLogger<IncomingAssociationResolver>.Instance);

        return new OfficeVersionSaveTestWebAppFactory(world).WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<EmailUploadCaptureService>();
            services.AddSingleton(new EmailUploadCaptureService(
                communications.Object, resolver, Mock.Of<ICommunicationEnrichmentService>(), ownership,
                NullLogger<EmailUploadCaptureService>.Instance));
            services.RemoveAll<ICommunicationDataverseService>();
            services.AddSingleton<ICommunicationDataverseService>(communications.Object);
        }));
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<List<string>> Names(HttpResponseMessage response, string property)
    {
        var body = await Body(response);
        body.TryGetProperty(property, out var names).Should().BeTrue($"the answer names {property}");
        return names.EnumerateArray().Select(e => e.GetString()!).ToList();
    }
}
