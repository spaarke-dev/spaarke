using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 138 (#2209), fix round 1: an email re-save that could add attachments to an already saved
/// <c>.eml</c> fails CLOSED, end to end through <c>POST /api/office/save</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The <c>.eml</c> must still be filed to the REQUEST's record (both link families). The match to the earlier save is made
/// by an idempotency key, which the client may send itself and which says nothing about where the email is filed.</item>
/// <item>The caller's rights on the <c>.eml</c> are checked BEFORE the app reads anything about it.</item>
/// <item>The refusal's own text reaches the response as the problem's <c>detail</c>.</item>
/// <item>A rights check that faults, a record whose documents now live in another container, and a stored-file read that fails
/// each end in a refusal or the duplicate answer, with nothing queued.</item>
/// </list>
/// Module-boundary doubles only (ADR-038 §4), the same world as <c>OfficeEmailReSaveAddsAttachmentsTests</c>.
/// </remarks>
[Trait("status", "task-138-word-add-in-r1")]
public class OfficeEmailReSaveFailClosedTests
{
    private const string RfcId = "<CAF0-138-failclosed@mail.example.com>";
    private const string A = "Settlement terms.pdf";
    private const string B = "Exhibit A.pdf";
    private const string SharedKey = "client-chosen-key-138";

    private static readonly SaveEntityReference MatterX = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("13813813-0138-4138-8138-0000000000a1"),
    };

    private static readonly SaveEntityReference MatterY = new()
    {
        EntityType = "matter",
        EntityId = Guid.Parse("13813813-0138-4138-8138-0000000000b2"),
    };

    // ── A. The .eml must still be filed to the request's record ─────────────────────────────────────────────────────

    [Fact]
    public async Task ReSave_ToAnotherRecord_WithTheKeyOfAnEarlierSave_AddsNothing_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        await FirstSaveWithChildren(client, world, MatterX, A);

        // Same client-chosen key, but the request files to ANOTHER record: the key finds the save that filed to X.
        var response = await client.SendAsync(Post(EmailSave(MatterY, new List<string> { A, B }), "to-y"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the stored .eml is filed to X, not to this request's record");
        (await Body(response)).GetProperty("duplicate").GetBoolean().Should().BeTrue();
        world.FinalizationPayloads.Should().ContainSingle("no add job is queued");
        world.Jobs.Should().ContainSingle();
    }

    [Fact]
    public async Task ReSave_ForAnEmlRefiledSince_AddsNothing_InEitherLinkFamily()
    {
        foreach (var refileInRelatedFamily in new[] { false, true })
        {
            var world = new OfficeVersionSaveWorld();
            using var factory = new OfficeVersionSaveTestWebAppFactory(world);
            var client = factory.CreateClient();
            var emlId = await FirstSaveWithChildren(client, world, MatterX, A);

            // The .eml is moved to Y after the save: its sprk_matter, or its sprk_relatedmatter, now names Y.
            if (refileInRelatedFamily)
                world.Documents[emlId].RelatedMatterId = MatterY.EntityId;
            else
                world.Documents[emlId].MatterId = MatterY.EntityId;

            var response = await client.SendAsync(Post(EmailSave(MatterX, new List<string> { A, B }), "refiled"));

            response.StatusCode.Should().Be(HttpStatusCode.OK, $"re-filed in the related family: {refileInRelatedFamily}");
            (await Body(response)).GetProperty("duplicate").GetBoolean().Should().BeTrue();
            world.FinalizationPayloads.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task ReSave_ForAnEmlFiledOnlyInTheRelatedFamily_ToTheRequestsRecord_StillAddsTheMissingAttachment()
    {
        // The positive control: the check reads both families, so a filing that sits only in sprk_relatedmatter counts.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, MatterX, A);
        world.Documents[emlId].MatterId = null;
        world.Documents[emlId].RelatedMatterId = MatterX.EntityId;

        var response = await client.SendAsync(Post(EmailSave(MatterX, new List<string> { A, B }), "related-only"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.FinalizationPayloads.Should().HaveCount(2);
    }

    // ── B. The caller's rights are checked before any app read ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReSave_ByACallerWithoutWriteOnTheEml_IsRefused_BeforeTheAppReadsAnything()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, MatterX, A);
        world.AccessByDocumentId[emlId] = AccessRights.Read;
        var readsBefore = world.DocumentReads.Count;
        var downloadsBefore = world.DownloadCalls;

        var response = await client.SendAsync(Post(EmailSave(MatterX, new List<string> { A, B }), "no-write"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Body(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        world.DocumentReads.Count.Should().Be(readsBefore, "the .eml row is not read for a caller who may not add to it");
        world.DownloadCalls.Should().Be(downloadsBefore, "the .eml's bytes are not downloaded for a caller who may not add to it");
        world.FinalizationPayloads.Should().ContainSingle();
        world.Jobs.Should().ContainSingle("no add job row is written for a refused caller");
    }

    [Fact]
    public async Task ReSave_ByACallerWithoutWrite_NamingOnlyWhatIsSaved_IsRefused_BecauseItCouldAdd()
    {
        // "Could add" is decided from the request alone (an Email, naming attachments, not none): whatever is stored.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, MatterX, A);
        world.AccessByDocumentId[emlId] = AccessRights.Read;

        var response = await client.SendAsync(Post(EmailSave(MatterX, new List<string> { A }), "no-write-a"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        world.FinalizationPayloads.Should().ContainSingle();
        world.Jobs.Should().ContainSingle("no add job row is written for a refused caller");
    }

    [Fact]
    public async Task ReSave_SelectingNone_ByACallerWithoutWrite_IsStillTheDuplicateAnswer()
    {
        // A request that could not add (none selected) never reaches the check: the older duplicate answer is unchanged.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, MatterX, A);
        world.AccessByDocumentId[emlId] = AccessRights.Read;

        var response = await client.SendAsync(Post(EmailSave(MatterX, new List<string>()), "none"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Body(response)).GetProperty("duplicate").GetBoolean().Should().BeTrue();
    }

    // ── C. The refusal's own text reaches the user ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReSave_Refused_CarriesItsOwnMessageAsTheProblemDetail()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var emlId = await FirstSaveWithChildren(client, world, MatterX, A);
        world.AccessByDocumentId[emlId] = AccessRights.Read;

        var response = await client.SendAsync(Post(EmailSave(MatterX, new List<string> { A, B }), "detail"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await Body(response);
        body.GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        body.GetProperty("detail").GetString().Should().Be(
            "You no longer have permission to add to this saved email, so nothing was saved.",
            "the pane shows the detail, so the refusal says what it refused and that nothing was saved, not the generic sentence");
    }

    // ── I. The fail-closed branches ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReSave_WhenTheAccessDecisionFaults_IsRefused_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        await FirstSaveWithChildren(factory.CreateClient(), world, MatterX, A);

        var faulting = new Mock<IAuthorizationService>();
        faulting
            .Setup(a => a.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test: the access decision faulted."));
        using var faulted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuthorizationService>();
            services.AddScoped(_ => faulting.Object);
        }));

        var response = await faulted.CreateClient().SendAsync(Post(EmailSave(MatterX, new List<string> { A, B }), "faulted"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a decision that cannot be made refuses");
        faulting.Verify(a => a.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        world.FinalizationPayloads.Should().ContainSingle();
        world.Jobs.Should().ContainSingle();
    }

    [Fact]
    public async Task ReSave_WhenTheRecordsDocumentsNowLiveInAnotherContainer_IsTheDuplicateAnswer_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        await FirstSaveWithChildren(client, world, MatterX, A);

        // The record became secure since the first save: its documents now belong in its own container, not the .eml's.
        world.SecureRecords[("sprk_matter", MatterX.EntityId)] = "b!secure-container-now";

        var response = await client.SendAsync(Post(EmailSave(MatterX, new List<string> { A, B }), "moved"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Body(response)).GetProperty("duplicate").GetBoolean().Should().BeTrue();
        world.FinalizationPayloads.Should().ContainSingle("nothing is added where the record's documents no longer live");
        world.Jobs.Should().ContainSingle();
    }

    [Fact]
    public async Task ReSave_WhenTheStoredFileCannotBeRead_IsTheDuplicateAnswer_AndQueuesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        await FirstSaveWithChildren(client, world, MatterX, A);
        world.FailDownloads = true;

        var response = await client.SendAsync(Post(EmailSave(MatterX, new List<string> { A, B }), "unreadable"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Body(response)).GetProperty("duplicate").GetBoolean().Should().BeTrue();
        world.FinalizationPayloads.Should().ContainSingle();
        world.Jobs.Should().ContainSingle();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The first save to <paramref name="target"/>, with only <paramref name="saved"/> ticked, then the children its worker would create.</summary>
    private static async Task<Guid> FirstSaveWithChildren(
        HttpClient client, OfficeVersionSaveWorld world, SaveEntityReference target, params string[] saved)
    {
        (await client.SendAsync(Post(EmailSave(target, saved.ToList()), "first-save"))).StatusCode
            .Should().Be(HttpStatusCode.Accepted);
        var emlId = world.Documents.Values.Should()
            .ContainSingle(d => d.FileName != null && d.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).Subject.Id;
        foreach (var name in saved)
            world.SeedAttachmentChild(emlId, name);
        return emlId;
    }

    /// <summary>An email save under the client's OWN key (the body key), the same message every time.</summary>
    private static SaveRequest EmailSave(SaveEntityReference target, List<string>? selection) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = target,
        IdempotencyKey = SharedKey,
        Email = new EmailMetadata
        {
            Subject = "RE: Settlement",
            SenderEmail = "counsel@test.com",
            SentDate = new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero),
            InternetMessageId = RfcId,
            Body = "<p>Attached.</p>",
            IsBodyHtml = true,
            IsNameSystemDerived = true,
            Attachments = new List<AttachmentReference> { Pdf(A), Pdf(B) },
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
}
