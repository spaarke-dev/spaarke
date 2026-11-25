using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 138 (#2209), end to end through <c>POST /api/office/save</c>: an email already saved to a
/// record, saved again to the same record with attachments ticked that were not saved the first time.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The re-save queues an attachments-only finalization for exactly the missing attachments under the EXISTING
/// <c>.eml</c> (no second <c>.eml</c>, no upload, no document create here; the worker creates the children — its own suite,
/// <c>UploadFinalizationAttachmentsOnlyTests</c>) and answers 202 with <c>attachmentsAdded</c>.</item>
/// <item>A re-save that names nothing new keeps today's "already saved" answer and queues nothing.</item>
/// <item>The add writes now, so the caller's CURRENT rights decide: no longer "write" on the <c>.eml</c>, or no longer able
/// to file to the record, is refused and nothing is queued.</item>
/// </list>
/// <para>What is "already saved" comes from the stored <c>.eml</c> (the real converter and filter over the bytes the first save
/// stored) and its children in the world, never from the request. Module-boundary doubles only (ADR-038 §4): the real route
/// and <c>OfficeService</c> over <see cref="OfficeVersionSaveWorld"/>. Each request carries its own <c>X-Idempotency-Key</c>,
/// as the pane's key differs with the selection, so the response cache never answers for the service.</para>
/// </remarks>
[Trait("status", "task-138-word-add-in-r1")]
public class OfficeEmailReSaveAddsAttachmentsTests
{
    private const string RfcId = "<CAF0-138-resave@mail.example.com>";
    private const string A = "Settlement terms.pdf";
    private const string B = "Exhibit A.pdf";
    private const string C = "Exhibit B.pdf";

    private static readonly SaveEntityReference Matter = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("13813813-0138-4138-8138-0000000000ee"),
    };

    [Fact]
    public async Task ReSave_NamingAMissingAttachment_QueuesExactlyThatOne_UnderTheExistingEml_AndNoSecondEml()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);

        var response = await client.SendAsync(Post(EmailSave(new List<string> { A, B }), "resave-ab"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "a new job adds the missing attachment");
        (await AttachmentsAdded(response)).Should().Equal(B);
        world.DocumentCreates.Should().Be(1, "no second .eml; the save itself creates nothing");
        world.SpeItems.Values.Count(i => i.Name.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
        world.Jobs.Should().HaveCount(2);

        var add = world.FinalizationPayloads.Should().HaveCount(2).And.Subject.Last();
        Bool(add, "AttachmentsOnly").Should().BeTrue();
        OfficeVersionSaveWorld.PayloadValue(add, "DocumentId").Should().Be(emlId.ToString(), "under the existing .eml");
        Selection(add).Should().Equal(B);
        OfficeVersionSaveWorld.PayloadValue(add, "OwningTeamId").Should().NotBeNullOrEmpty("the children are created team-owned");
    }

    [Fact]
    public async Task ReSave_NamingOnlyWhatIsSaved_IsAlreadySaved_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        await FirstSaveWithChildren(client, world, A);

        var response = await client.SendAsync(Post(EmailSave(new List<string> { A }), "resave-a"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Body(response);
        body.GetProperty("duplicate").GetBoolean().Should().BeTrue();
        body.TryGetProperty("attachmentsAdded", out _).Should().BeFalse();
        world.FinalizationPayloads.Should().ContainSingle("the first save's only");
        world.Jobs.Should().ContainSingle();
    }

    [Fact]
    public async Task ReSave_OfEveryAttachment_QueuesEachOneNotYetSaved_AndOnlyThose()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        await FirstSaveWithChildren(client, world, A);

        var response = await client.SendAsync(Post(EmailSave(selection: null), "resave-all"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await AttachmentsAdded(response)).Should().BeEquivalentTo(new[] { B, C });
        Selection(world.FinalizationPayloads.Last()).Should().BeEquivalentTo(new[] { B, C });
    }

    [Fact]
    public async Task ReSave_SelectingNone_IsAlreadySaved_AsBefore()
    {
        // The from-email route forces []: empty means none, so there is never an add.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        await FirstSaveWithChildren(client, world, A);

        var response = await client.SendAsync(Post(EmailSave(new List<string>()), "resave-none"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Body(response)).GetProperty("duplicate").GetBoolean().Should().BeTrue();
        world.FinalizationPayloads.Should().ContainSingle();
    }

    [Fact]
    public async Task ReSave_ByACallerWhoLostWriteOnTheSavedEmail_IsRefused_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        world.AccessByDocumentId[emlId] = AccessRights.Read; // access changed since the first save

        var response = await client.SendAsync(Post(EmailSave(new List<string> { A, B }), "resave-no-write"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        world.FinalizationPayloads.Should().ContainSingle("no child is queued");
        world.Jobs.Should().ContainSingle("refused before the job row");
        world.DocumentCreates.Should().Be(1);
    }

    [Fact]
    public async Task ReSave_ByACallerWhoMayNoLongerFileToTheRecord_IsRefused_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        await FirstSaveWithChildren(factory.CreateClient(), world, A);

        // The same world, the caller now able only to READ the record (EntityAccessFilter's question on every request).
        using var readOnly = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddScoped(_ => ComposeServiceCollaborators.Probe(ComposeServiceCollaborators.ReadOnly).Object);
        }));

        var response = await readOnly.CreateClient().SendAsync(Post(EmailSave(new List<string> { A, B }), "resave-no-record"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        world.FinalizationPayloads.Should().ContainSingle();
        world.Jobs.Should().ContainSingle();
    }

    [Fact]
    public async Task ReSave_AfterTheSavedEmailWasDeleted_IsAlreadySaved_AndCreatesNoOrphan()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, A);
        world.Documents.Remove(emlId);

        var response = await client.SendAsync(Post(EmailSave(new List<string> { A, B }), "resave-deleted"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        world.FinalizationPayloads.Should().ContainSingle();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The first save, with only <paramref name="saved"/> ticked, then the children its worker would create.</summary>
    private static async Task<Guid> FirstSaveWithChildren(HttpClient client, OfficeVersionSaveWorld world, params string[] saved)
    {
        (await client.SendAsync(Post(EmailSave(saved.ToList()), "first-save"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var emlId = world.Documents.Values.Should()
            .ContainSingle(d => d.FileName != null && d.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Subject.Id;
        foreach (var name in saved)
            world.SeedAttachmentChild(emlId, name);
        return emlId;
    }

    private static SaveRequest EmailSave(List<string>? selection) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = Matter,
        Email = new EmailMetadata
        {
            Subject = "RE: Settlement",
            SenderEmail = "counsel@test.com",
            SentDate = new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero),
            InternetMessageId = RfcId,
            Body = "<p>Attached.</p>",
            IsBodyHtml = true,
            IsNameSystemDerived = true,
            Attachments = new List<AttachmentReference> { Pdf(A), Pdf(B), Pdf(C) },
            SelectedAttachmentFileNames = selection,
        },
    };

    private static AttachmentReference Pdf(string name) => new()
    {
        AttachmentId = $"att-{name}",
        FileName = name,
        ContentType = "application/pdf",
        Size = 20_000,
        ContentBase64 = Convert.ToBase64String(new byte[20_000]),
    };

    private static HttpRequestMessage Post(SaveRequest body, string headerKey)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save") { Content = JsonContent.Create(body) };
        message.Headers.Add("X-Idempotency-Key", headerKey);
        return message;
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<List<string>> AttachmentsAdded(HttpResponseMessage response)
    {
        var body = await Body(response);
        body.TryGetProperty("attachmentsAdded", out var added).Should().BeTrue("the answer names what this save adds");
        return added.EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool Bool(JsonElement payload, string name) =>
        Property(payload, name) is { ValueKind: JsonValueKind.True };

    private static List<string> Selection(JsonElement payload) =>
        Property(Property(payload, "EmailMetadata"), "SelectedAttachmentFileNames").EnumerateArray()
            .Select(e => e.GetString()!).ToList();
}
