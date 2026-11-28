using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Documents;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 143 review (F1): an attachment saved on its own is STORED under its file name with task 046 (b)'s
/// short unique suffix, because that name comes from the email (never typed in the pane) and so is system-derived (owner decision
/// B2, 2026-09-14/15). It is SHOWN (<c>sprk_documentname</c>) under the attachment's own file name, unsanitized and unsuffixed, as
/// the worker names an <c>.eml</c>'s attachment children. Containers are business-unit scoped and flat, so before this a second,
/// different attachment with a common name in the same container was refused 409 <c>OFFICE_020</c>.
/// </summary>
/// <remarks>
/// Module-boundary doubles only (ADR-038 §4): the real route and <c>OfficeService</c> over <see cref="OfficeVersionSaveWorld"/>,
/// whose drive is path-keyed exactly as the facade is; the identity route over the doubles
/// <c>DocumentEmailIdentityStandaloneAttachmentsContractTests</c> uses, fed with the rows the saves wrote.
/// </remarks>
[Trait("status", "task-143-word-add-in-r1")]
public class OfficeAttachmentSaveNamingTests
{
    /// <summary>Where a matter-targeted save lands in this fixture (<c>EmailProcessing:DefaultContainerId</c>).</summary>
    private const string SaveContainer = "b!test-office-save-drive";

    private const string MessageId = "<CAF0-143-naming@mail.example.com>";

    private static readonly byte[] FirstBytes = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
    private static readonly byte[] SecondBytes = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };

    /// <summary>The stored form: the sanitized stem, <c>_</c>, 8 lower-case hex digits, then the extension.</summary>
    private static string StoredNamePattern(string stem, string extension) =>
        $"^{System.Text.RegularExpressions.Regex.Escape(stem)}_[0-9a-f]{{8}}{System.Text.RegularExpressions.Regex.Escape(extension)}$";

    // ── Must fire ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoAttachmentOnlySaves_OfDifferentBytes_WithTheSameFileName_InOneContainer_BothSucceed_UnderDistinctStoredNames()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/office/save", AttachmentOnly("Exhibit A.pdf", FirstBytes));
        var second = await client.PostAsJsonAsync("/api/office/save", AttachmentOnly("Exhibit A.pdf", SecondBytes));

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "a different attachment that shares a common name is a second file, never refused OFFICE_020");
        world.CollisionRefusals.Should().Be(0, "each save asks for its own stored name");
        world.DocumentCreates.Should().Be(2);

        var rows = world.Documents.Values.ToList();
        rows.Select(r => r.DocumentName).Should().AllBe("Exhibit A.pdf", "the document is shown under the attachment's own name");
        rows.Select(r => r.FileName).Should().OnlyHaveUniqueItems()
            .And.AllSatisfy(name => name.Should().MatchRegex(StoredNamePattern("Exhibit A", ".pdf")));

        var items = world.SpeItems.Values.ToList();
        items.Should().HaveCount(2).And.OnlyContain(i => i.DriveId == items[0].DriveId, "both are in the one container");
        items.Select(i => i.Name).Should().BeEquivalentTo(rows.Select(r => r.FileName));
        items.Select(i => i.Versions.Single()).Should().BeEquivalentTo(new[] { FirstBytes, SecondBytes },
            "each file holds its own attachment; neither was overwritten");
    }

    [Fact]
    public async Task AttachmentSave_ToARecord_UnderAnExistingDocumentsName_IsStoredUnderItsOwnSuffixedName_AndLeavesThatFileIntact()
    {
        var world = new OfficeVersionSaveWorld();
        var (_, existingItemId) = world.SeedDocument(SaveContainer, "Exhibit A.pdf", FirstBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save",
            AttachmentOnly("Exhibit A.pdf", SecondBytes) with
            {
                TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
            });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.CollisionRefusals.Should().Be(0, "the stored name is not the existing document's");
        world.SpeItems[existingItemId].Versions.Should().ContainSingle().Which.Should().Equal(FirstBytes);
        world.CreatedDocumentNames.Should().ContainSingle().Which.Should().Be("Exhibit A.pdf");
    }

    [Fact]
    public async Task AttachmentSave_WhoseNameTheSanitizerChanges_IsShownUnderItsOriginalName_AndStoredSanitizedAndSuffixed()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", AttachmentOnly("Q3: results/final.pdf", FirstBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var row = world.Documents.Values.Should().ContainSingle().Subject;
        row.DocumentName.Should().Be("Q3: results/final.pdf", "sprk_documentname is the attachment's own file name");
        row.FileName.Should().MatchRegex(StoredNamePattern("Q3 resultsfinal", ".pdf"),
            "the stored name is still sanitized (no folder is minted) and carries the suffix");
        world.SpeItems.Values.Should().ContainSingle().Which.Name.Should().Be(row.FileName);
    }

    [Fact]
    public async Task TheIdentityRoute_ReportsEachAttachmentSavedOnItsOwn_UnderTheAttachmentsOwnFileName()
    {
        // Task 141's name rule (EmailAttachmentAddPlanner.SavedNamesOf: documentname or filename, case-insensitive) is what the
        // pane marks an attachment saved by. The stored name now carries a suffix, so the document name must be the attachment's
        // own file name; that also covers a name the sanitizer changed, which before this was never reported under its own name.
        var world = new OfficeVersionSaveWorld();
        using (var factory = new OfficeVersionSaveTestWebAppFactory(world))
        {
            var client = factory.CreateClient();
            (await client.PostAsJsonAsync("/api/office/save", AttachmentOnly("Exhibit A.pdf", FirstBytes)))
                .StatusCode.Should().Be(HttpStatusCode.Accepted);
            (await client.PostAsJsonAsync("/api/office/save", AttachmentOnly("Exhibit A.pdf", SecondBytes)))
                .StatusCode.Should().Be(HttpStatusCode.Accepted);
            (await client.PostAsJsonAsync("/api/office/save", AttachmentOnly("Q3: results.pdf", FirstBytes.Reverse().ToArray())))
                .StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        // The rows the saves wrote, as the standalone lookup reads them (sprk_documentname, sprk_filename).
        var standalone = world.Documents.Values.Select(row =>
        {
            var entity = new Entity("sprk_document", row.Id);
            if (row.DocumentName is not null)
                entity["sprk_documentname"] = row.DocumentName;
            if (row.FileName is not null)
                entity["sprk_filename"] = row.FileName;
            return entity;
        }).ToList();
        standalone.Should().HaveCount(3);

        using var identity = IdentityHost(standalone);
        var names = await SavedAttachmentNames(identity);

        names.Should().Contain("Exhibit A.pdf", "both saves of the shared name are found under it");
        names.Should().Contain("Q3: results.pdf", "a name the sanitizer changed is found under the attachment's own name");
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AWordDocumentSave_KeepsItsNaming_NoSuffix()
    {
        // A name the user typed is never changed (owner decision B2): a Document save is stored under its sanitized file name
        // exactly and shown under its title, as task 020 left it.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", FirstBytes, title: "Acme Brief"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.SpeItems.Values.Should().ContainSingle().Which.Name.Should().Be("Brief.docx");
        world.CreatedDocumentNames.Should().ContainSingle().Which.Should().Be("Acme Brief");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One attachment saved on its own, as the pane sends it: its bytes and name, no email, no record.</summary>
    private static SaveRequest AttachmentOnly(string fileName, byte[] bytes) => new()
    {
        ContentType = SaveContentType.Attachment,
        TargetEntity = null,
        Attachment = new AttachmentMetadata
        {
            AttachmentId = $"att-{Guid.NewGuid():N}",
            FileName = fileName,
            ContentBase64 = Convert.ToBase64String(bytes),
        },
    };

    /// <summary>
    /// <c>resolve-email-identity</c> with no saved <c>.eml</c> and <paramref name="standalone"/> as the email's standalone
    /// attachment documents, every one readable by the caller (as <c>DocumentEmailIdentityStandaloneAttachmentsContractTests</c>).
    /// </summary>
    private static WebApplicationFactory<Program> IdentityHost(List<Entity> standalone)
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => IsArchiveLookup(q)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>()));
        entityService
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => IsStandaloneLookup(q)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(standalone));

        var accessSource = new Mock<IAccessDataSource>();
        accessSource
            .Setup(a => a.GetUserAccessAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((string user, string resourceId, string? _, CancellationToken _) => Task.FromResult(new AccessSnapshot
            {
                UserId = user,
                ResourceId = resourceId,
                AccessRights = AccessRights.Read,
            }));

        var factory = new DocumentIdentityTestWebAppFactory(
            // Placeholder: this route never reaches SharePoint Embedded.
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor("unused-drive", "unused-item")),
            entityService,
            accessSource);
        return factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(new Mock<IDataverseService>().Object);
        }));
    }

    private static bool IsArchiveLookup(QueryExpression q) =>
        q.EntityName == "sprk_document"
        && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_isemailarchive" && c.Operator == ConditionOperator.Equal
            && Equals(c.Values.SingleOrDefault(), true));

    private static bool IsStandaloneLookup(QueryExpression q) =>
        q.EntityName == "sprk_document"
        && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_parentdocument" && c.Operator == ConditionOperator.Null);

    private static async Task<List<string>> SavedAttachmentNames(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        var response = await client.PostAsJsonAsync("/api/documents/resolve-email-identity", new { internetMessageId = MessageId });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("reason").GetString().Should().Be("not_saved");
        return body.GetProperty("savedAttachmentNames").EnumerateArray().Select(e => e.GetString()!).ToList();
    }
}
