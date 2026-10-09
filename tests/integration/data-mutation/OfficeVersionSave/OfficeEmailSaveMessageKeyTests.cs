using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using MimeKit;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 121, end to end through <c>POST /api/office/save</c>: an email save that carries the RFC
/// Message-ID (<c>internetMessageId</c>) and the Exchange item id (<c>exchangeItemId</c>) separately.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The stored <c>.eml</c> carries the Message-ID header, and the finalization job is queued with the RFC id (the
/// worker writes it to the email artifact and the attachment documents).</item>
/// <item>A draft (no Message-ID yet) still stores its item id, exactly as a save did before task 121.</item>
/// <item>Two recipients saving the same email to the same record are two saves. The RFC id is the same in every
/// mailbox, so it gives them the same server idempotency key; the persistent duplicate check is the caller's own.
/// Before this, the second user was answered with the first user's job, which they may not read, and got no
/// document.</item>
/// </list>
/// <para>Module-boundary doubles only (ADR-038 §4): the real route and <c>OfficeService</c> over
/// <see cref="OfficeVersionSaveWorld"/>.</para>
/// </remarks>
[Trait("status", "task-121-word-add-in-r1")]
public class OfficeEmailSaveMessageKeyTests
{
    private const string RfcId = "<CAF0a1b2c3@mail.example.com>";
    private const string ItemId = "AAMkAGI2TG93AAA=";
    private const string OtherRecipientOid = "7d1c3a52-6a51-4c4e-9a3e-2f1b4c5d6e7f";

    private static SaveRequest EmailSave(string? internetMessageId, string? exchangeItemId, SaveEntityReference? record = null) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = record ?? new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
        Email = new EmailMetadata
        {
            Subject = "RE: Discovery schedule",
            SenderEmail = "counsel@test.com",
            SentDate = new DateTimeOffset(2026, 10, 8, 9, 30, 0, TimeSpan.Zero),
            InternetMessageId = internetMessageId,
            ExchangeItemId = exchangeItemId,
            Body = "<p>The email text</p>",
            IsBodyHtml = true,
            IsNameSystemDerived = true,
        },
    };

    private static HttpRequestMessage Post(SaveRequest body, string? headerKey = null, string? oid = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save") { Content = JsonContent.Create(body) };
        if (headerKey is not null)
            message.Headers.Add("X-Idempotency-Key", headerKey);
        if (oid is not null)
            message.Headers.Add("X-Test-Oid", oid);
        return message;
    }

    private static MimeMessage StoredEml(OfficeVersionSaveWorld world)
    {
        var item = world.SpeItems.Values.Should()
            .ContainSingle(i => i.Name.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Subject;
        return MimeMessage.Load(new MemoryStream(item.Versions.Should().ContainSingle().Subject));
    }

    private static string? QueuedMessageId(OfficeVersionSaveWorld world)
    {
        var payload = world.FinalizationPayloads.Should().ContainSingle().Subject;
        var email = payload.EnumerateObject()
            .Single(p => string.Equals(p.Name, "EmailMetadata", StringComparison.OrdinalIgnoreCase)).Value;
        return OfficeVersionSaveWorld.PayloadValue(email, "InternetMessageId");
    }

    [Fact]
    public async Task EmailSave_WithBothKeys_WritesTheMessageIdHeader_AndQueuesTheRfcId()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        (await client.SendAsync(Post(EmailSave(RfcId, ItemId)))).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var eml = StoredEml(world);
        eml.MessageId.Should().Be("CAF0a1b2c3@mail.example.com");
        eml.Headers.Contains("X-Exchange-Item-Id").Should().BeFalse();
        QueuedMessageId(world).Should().Be(RfcId);
    }

    [Fact]
    public async Task DraftEmailSave_WithOnlyTheItemId_KeepsTheItemIdAsBefore()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        (await client.SendAsync(Post(EmailSave(internetMessageId: null, ItemId)))).StatusCode.Should().Be(HttpStatusCode.Accepted);

        StoredEml(world).Headers["X-Exchange-Item-Id"].Should().Be(ItemId);
        QueuedMessageId(world).Should().Be(ItemId, "a save without a Message-ID stores the item id, as every pane save did before");
    }

    [Fact]
    public async Task SameEmailSavedBySecondRecipient_ToTheSameRecord_IsANewSave_NotTheFirstUsersDuplicate()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var record = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() };

        (await client.SendAsync(Post(EmailSave(RfcId, ItemId, record)))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        // The other recipient's mailbox has its own item id; the Message-ID is the same.
        var second = await client.SendAsync(Post(EmailSave(RfcId, "AAMkOTHERMAILBOX=", record), oid: OtherRecipientOid));

        second.StatusCode.Should().Be(HttpStatusCode.Accepted, "another user's job is not this caller's duplicate");
        (await second.Content.ReadFromJsonAsync<SaveResponse>())!.Duplicate.Should().BeFalse();
        world.Jobs.Should().HaveCount(2);
        world.Jobs.Select(j => j.IdempotencyKey).Distinct().Should().ContainSingle("precondition: both saves share the server key");
    }

    [Fact]
    public async Task FirstUserSavesAgain_AfterASecondRecipientsNewerSave_IsTheFirstUsersDuplicate()
    {
        // A, then B (a newer job under the same key), then A again: A's own job answers — B's newer row must not hide it.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var record = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() };

        (await client.SendAsync(Post(EmailSave(RfcId, ItemId, record), "alice-1"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.SendAsync(Post(EmailSave(RfcId, "AAMkOTHERMAILBOX=", record), "bob-1", OtherRecipientOid)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var again = await client.SendAsync(Post(EmailSave(RfcId, ItemId, record), "alice-2"));

        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<SaveResponse>())!.Duplicate.Should().BeTrue();
        world.Jobs.Should().HaveCount(2, "Alice's second save wrote nothing new");
    }

    [Fact]
    public async Task EmailSaveReconciledToAnUnfiledCommunication_FilesIt_AskingWithTheCallersOwnToken()
    {
        // Task 121 owner decision ("file it if unfiled"), through the real route and OfficeService: the communication's
        // Write right is asked with the CALLER's bearer token — the probe grants it for that token only.
        var communicationId = Guid.NewGuid();
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(d => d.CreateCommunicationRaceProofAsync(It.IsAny<DataverseEntity>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((communicationId, true));
        dataverse
            .Setup(d => d.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_communication", communicationId));
        var communicationWrites = new List<Dictionary<string, object>>();
        dataverse
            .Setup(d => d.UpdateAsync("sprk_communication", communicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => communicationWrites.Add(f))
            .Returns(Task.CompletedTask);
        var probe = new Mock<CallerRecordAccessProbe>(
            new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!)
        { CallBase = false };
        probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.None);
        probe.Setup(p => p.GetCallerRightsAsync("test-caller-token", "sprk_communications", communicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read | AccessRights.Write);

        var ownership = new RecordOwnershipResolverDouble();
        var options = new AutoFileOptions { Enabled = true, Threshold = 0.85 };
        var resolver = new IncomingAssociationResolver(
            new IAssociationRung[] { new ExplicitReferenceRung(dataverse.Object) }, dataverse.Object, dataverse.Object,
            new AssociationStatusMapper(new AutoFileGate(Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options)),
                NullLogger<AssociationStatusMapper>.Instance),
            CoreAncestorResolverFixtures.Inert(), ownership, NullLogger<IncomingAssociationResolver>.Instance);

        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world).WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<EmailUploadCaptureService>();
            services.AddSingleton(new EmailUploadCaptureService(
                dataverse.Object, resolver, Mock.Of<ICommunicationEnrichmentService>(), ownership,
                NullLogger<EmailUploadCaptureService>.Instance));
            services.RemoveAll<ReconciledEmailFiling>();
            services.AddScoped(sp => new ReconciledEmailFiling(
                resolver, dataverse.Object, probe.Object, sp.GetRequiredService<SecureChildReconciler>(),
                NullLogger<ReconciledEmailFiling>.Instance));
        }));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-caller-token");
        var matterId = Guid.NewGuid();

        (await client.SendAsync(Post(EmailSave(RfcId, ItemId, new SaveEntityReference { EntityType = "matter", EntityId = matterId }))))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        ((EntityReference)communicationWrites.Should().ContainSingle().Subject["sprk_regardingmatter"]).Id.Should().Be(matterId);
        probe.Verify(p => p.GetCallerRightsAsync("test-caller-token", "sprk_communications", communicationId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SameUserRetry_UnderADifferentHeaderKey_IsStillDeduplicatedByTheServerKey()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var request = EmailSave(RfcId, ItemId);

        (await client.SendAsync(Post(request, "attempt-a"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var retry = await client.SendAsync(Post(request, "attempt-b"));

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        (await retry.Content.ReadFromJsonAsync<SaveResponse>())!.Duplicate.Should().BeTrue();
        world.Jobs.Should().ContainSingle();
    }
}
