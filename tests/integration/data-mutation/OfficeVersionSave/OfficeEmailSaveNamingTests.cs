using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// The stored name of an Office email save (spaarkeai-word-add-in-r1 task 046 (b); owner decision 2026-09-15, B2).
/// </summary>
/// <remarks>
/// <para><b>The failure mode.</b> The <c>.eml</c> name was <c>{yyyy-MM-dd}_{subject}.eml</c>, and the create upload
/// is PATH-keyed under <c>Replace</c>. So a second email with the same subject and date, filed into the same container,
/// landed on the first email's item and silently replaced its file.</para>
/// <para><b>The rule.</b> A SYSTEM-DERIVED name (the email's own subject) is stored with a short unique suffix. A name
/// the user TYPED in the pane's Document Name box is never changed, and neither is an older client's name, which may
/// have been typed. <c>sprk_documentname</c> keeps the readable name in every case; only the stored file (the SPE item
/// and <c>sprk_filename</c>) carries the suffix.</para>
/// <para>Module-boundary doubles only (ADR-038 §4): the real route and <c>OfficeService</c> over
/// <see cref="OfficeVersionSaveWorld"/>, whose drive is path-keyed exactly as the facade is.</para>
/// </remarks>
[Trait("status", "repaired")]
public class OfficeEmailSaveNamingTests
{
    /// <summary>The readable name for subject "Re: Filing" sent 2026-09-14 (':' is stripped by the sanitizer).</summary>
    private const string ReadableName = "2026-09-14_Re Filing.eml";

    private const string SuffixedNamePattern = @"^2026-09-14_Re Filing_[0-9a-f]{8}\.eml$";

    private static readonly DateTimeOffset Sent = new(2026, 9, 14, 9, 30, 0, TimeSpan.Zero);

    private static SaveEntityReference NewRecord() => new() { EntityType = "matter", EntityId = Guid.NewGuid() };

    private static SaveRequest EmailSave(SaveEntityReference record, string messageId, string body, bool systemNamed) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = record,
        Email = new EmailMetadata
        {
            Subject = "Re: Filing",
            SenderEmail = "counsel@test.com",
            SentDate = Sent,
            InternetMessageId = messageId,
            Body = body,
            IsNameSystemDerived = systemNamed,
        },
    };

    private static List<OfficeVersionSaveWorld.SpeItem> EmlItems(OfficeVersionSaveWorld world) =>
        world.SpeItems.Values.Where(i => i.Name.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)).ToList();

    [Fact]
    public async Task TwoSystemNamedEmailSaves_WithTheSameSubjectAndDate_ToTheSameContainer_ProduceTwoDistinctFiles()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var record = NewRecord();

        (await client.PostAsJsonAsync("/api/office/save", EmailSave(record, "<first@test.com>", "First reply.", systemNamed: true)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync("/api/office/save", EmailSave(record, "<second@test.com>", "Second reply.", systemNamed: true)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var items = EmlItems(world);
        items.Should().HaveCount(2, "two different emails are two files");
        items.Should().OnlyContain(i => i.Versions.Count == 1, "neither save wrote over the other's file");
        items.Should().ContainSingle(i => Encoding.UTF8.GetString(i.Versions[0]).Contains("First reply."));
        items.Should().ContainSingle(i => Encoding.UTF8.GetString(i.Versions[0]).Contains("Second reply."));
        items.Select(i => i.Name).Should().OnlyContain(n => System.Text.RegularExpressions.Regex.IsMatch(n, SuffixedNamePattern));
        world.Documents.Values.Select(r => r.ItemId).Should().BeEquivalentTo(
            items.Select(i => i.Id), "each email's document points at its own file");
    }

    [Theory]
    [InlineData(false)] // the pane, when the user typed the Document Name
    [InlineData(null)]  // an older client that does not send the flag
    public async Task EmailSave_WhoseNameIsNotSystemDerived_IsStoredUnderTheReadableNameUnchanged(bool? flag)
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var body = JsonSerializer.SerializeToNode(
            EmailSave(NewRecord(), "<typed@test.com>", "Typed.", systemNamed: false),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if (flag is null)
        {
            body["email"]!.AsObject().Remove("isNameSystemDerived").Should().BeTrue(
                "precondition: the older client's body carries no flag at all");
        }

        var response = await factory.CreateClient().PostAsync(
            "/api/office/save", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        EmlItems(world).Should().ContainSingle().Which.Name.Should().Be(
            ReadableName, "a name the user typed is never changed automatically");
        world.CreatedDocumentNames.Should().ContainSingle().Which.Should().Be(ReadableName);
        world.DocumentUpdates.Where(u => u.Update.FileName is not null)
            .Should().ContainSingle().Which.Update.FileName.Should().Be(ReadableName);
    }

    [Fact]
    public async Task SystemNamedEmailSave_SuffixesOnlyTheStoredFile_AndKeepsTheDocumentNameReadable()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", EmailSave(NewRecord(), "<only@test.com>", "Only.", systemNamed: true));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var stored = EmlItems(world).Should().ContainSingle().Subject;
        stored.Name.Should().MatchRegex(SuffixedNamePattern, "the stored file carries the unique suffix");
        world.CreatedDocumentNames.Should().ContainSingle().Which.Should().Be(
            ReadableName, "sprk_documentname is the readable name, with no suffix");
        world.DocumentUpdates.Where(u => u.Update.FileName is not null)
            .Should().ContainSingle().Which.Update.FileName.Should().Be(
                stored.Name, "sprk_filename is the stored file's own name");
    }
}
