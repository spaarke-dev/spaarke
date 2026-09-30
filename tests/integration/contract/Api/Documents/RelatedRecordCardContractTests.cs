using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Tests.Api.Documents;
using Sprk.Bff.Api.Tests.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

/// <summary>
/// Contract coverage for the related-record DISPLAY fields task 026 (FR-09) added to
/// <c>POST /api/documents/resolve-identity</c> — <c>relatedRecord.displayName</c> and
/// <c>relatedRecord.number</c>, correctly labeled regardless of which attribute happens to be the entity's
/// Dataverse primary name (<c>notes/026-slot-scope-decision.md</c> §3).
/// </summary>
/// <remarks>
/// <para><b>Scope, relative to existing coverage.</b> <see cref="DocumentIdentityContractTests"/> (task 016)
/// already proves the base resolve-identity pipeline (auth, malformed URL, not-a-Spaarke-document, the
/// no-related-record 403). Every one of its fixture documents has NO populated matter/project/invoice/
/// workassignment lookup, so it never exercises the NEW complementary-field fetch this task adds.
/// <see cref="DocumentUrlIdentityResolutionDisplayFieldTests"/> (also task 026) proves the field-mapping
/// matrix directly against <c>ResolveRelatedRecordDisplayAsync</c>. THIS file is the one that proves the
/// full HTTP shape AND — the acceptance criterion existing coverage could not — that a populated related
/// record's name and number do NOT leak on a 403 when the caller lacks read on the document.</para>
/// <para><b>Fixture.</b> Reuses <see cref="DocumentIdentityTestWebAppFactory"/> as-is (CLAUDE.md §11 — the
/// existing factory already wires the three doubles this route's filters need; nothing here needed a change
/// to it).</para>
/// </remarks>
public class RelatedRecordCardContractTests
{
    private const string DocumentUrl =
        "https://spaarke.sharepoint.com/contentstorage/CSP_585db4c8-8043-4676-965e-c92e45f07221/Document Library/Examiner report draft.docx";
    private const string DriveId = "b!yLMdWD2AdkaWXsktRe9yIW7Hn0uXvZVBnuXhwwvLvZWY-YU6-G3sQ7t6c1tKzXJM";
    private const string ItemId = "01BYE5RZ6QN3ZWBTUFOFD3GSPGOHDJD36K";

    [Fact]
    public async Task ResolveIdentity_ForADocumentFiledToAMatter_ReturnsTypeDisplayNameAndNumber()
    {
        var documentId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var entityService = EntityServiceWithRelatedRecord(
            documentId, "sprk_matter", matterId, entityReferenceName: "PAT-191111");
        entityService
            .Setup(e => e.RetrieveAsync(
                "sprk_matter", matterId, It.Is<string[]>(c => c.Length == 1 && c[0] == "sprk_mattername"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", matterId) { ["sprk_mattername"] = "Acme v Globex" });

        using var factory = new DocumentIdentityTestWebAppFactory(
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor(DriveId, ItemId)),
            entityService,
            GrantingAccessSource(documentId));
        var client = AuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/documents/resolve-identity", new { documentUrl = DocumentUrl });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var related = body.GetProperty("relatedRecord");
        related.GetProperty("entityType").GetString().Should().Be("sprk_matter");
        related.GetProperty("id").GetString().Should().Be(matterId.ToString("D"));
        related.GetProperty("number").GetString().Should().Be("PAT-191111", "sprk_matter's primary name IS its number");
        related.GetProperty("displayName").GetString().Should().Be("Acme v Globex");
    }

    [Fact]
    public async Task ResolveIdentity_ForADocumentFiledToAnInvoice_ReturnsTypeDisplayNameAndNumber()
    {
        var documentId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var entityService = EntityServiceWithRelatedRecord(
            documentId, "sprk_invoice", invoiceId, entityReferenceName: "March retainer");
        entityService
            .Setup(e => e.RetrieveAsync(
                "sprk_invoice", invoiceId, It.Is<string[]>(c => c.Length == 1 && c[0] == "sprk_invoicenumber"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", invoiceId) { ["sprk_invoicenumber"] = "INV-000482" });

        using var factory = new DocumentIdentityTestWebAppFactory(
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor(DriveId, ItemId)),
            entityService,
            GrantingAccessSource(documentId));
        var client = AuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/documents/resolve-identity", new { documentUrl = DocumentUrl });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var related = body.GetProperty("relatedRecord");
        related.GetProperty("entityType").GetString().Should().Be("sprk_invoice");
        related.GetProperty("displayName").GetString().Should().Be("March retainer", "sprk_invoice's primary name IS its descriptive name");
        related.GetProperty("number").GetString().Should().Be("INV-000482");
    }

    [Fact]
    public async Task ResolveIdentity_ForAPaneCreatedMatterWithNoNumberYet_ReturnsANullNumber_NotAnError()
    {
        // notes/030-numbering-handoff.md: a Matter created from the pane has a BLANK sprk_matternumber until
        // the separate numbering project ships — EntityReference.Name is then null/empty.
        var documentId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var entityService = EntityServiceWithRelatedRecord(
            documentId, "sprk_matter", matterId, entityReferenceName: null);
        entityService
            .Setup(e => e.RetrieveAsync(
                "sprk_matter", matterId, It.Is<string[]>(c => c.Length == 1 && c[0] == "sprk_mattername"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", matterId) { ["sprk_mattername"] = "Acme v Globex" });

        using var factory = new DocumentIdentityTestWebAppFactory(
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor(DriveId, ItemId)),
            entityService,
            GrantingAccessSource(documentId));
        var client = AuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/documents/resolve-identity", new { documentUrl = DocumentUrl });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var related = body.GetProperty("relatedRecord");
        related.GetProperty("number").ValueKind.Should().Be(JsonValueKind.Null);
        related.GetProperty("displayName").GetString().Should().Be("Acme v Globex");
    }

    [Fact]
    public async Task ResolveIdentity_WhenCallerLacksReadOnADocumentFiledToAMatter_Returns403_LeaksNoNameOrNumber()
    {
        // The negative case existing coverage (DocumentIdentityContractTests) could not prove: THIS document
        // has a populated, name-and-number-bearing related record, so there is something that COULD leak.
        var documentId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        // STRICT: no RetrieveAsync setup for the complementary field — if the handler ever fetched the
        // display fields before authorization denied, this test fails on the unexpected call, proving the
        // extra Dataverse round-trip is gated on the SAME document-read check (notes/026-slot-scope-decision.md
        // §6: record access <=> document access, so no second check is re-derived for the related record).
        var entityService = EntityServiceWithRelatedRecord(
            documentId, "sprk_matter", matterId, entityReferenceName: "PAT-191111");

        var accessSource = new Mock<IAccessDataSource>();
        accessSource
            .Setup(a => a.GetUserAccessAsync(
                It.IsAny<string>(), documentId.ToString("D"), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot
            {
                UserId = "caller",
                ResourceId = documentId.ToString("D"),
                AccessRights = AccessRights.None,
            });

        using var factory = new DocumentIdentityTestWebAppFactory(
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor(DriveId, ItemId)),
            entityService,
            accessSource);
        var client = AuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/documents/resolve-identity", new { documentUrl = DocumentUrl });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("PAT-191111");
        raw.Should().NotContain("Acme");
        raw.Should().NotContain(matterId.ToString("D"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("relatedRecord", out _).Should().BeFalse();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static HttpClient AuthenticatedClient(DocumentIdentityTestWebAppFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }

    private static Mock<IGenericEntityService> EntityServiceWithRelatedRecord(
        Guid documentId, string relatedLogicalName, Guid relatedId, string? entityReferenceName)
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", documentId)
            {
                ["sprk_documentname"] = "Examiner report draft",
                ["sprk_filename"] = "Examiner report draft.docx",
                ["sprk_graphdriveid"] = DriveId,
                [relatedLogicalName] = new EntityReference(relatedLogicalName, relatedId) { Name = entityReferenceName },
            });
        return entityService;
    }

    private static Mock<IAccessDataSource> GrantingAccessSource(Guid documentId)
    {
        var accessSource = new Mock<IAccessDataSource>();
        accessSource
            .Setup(a => a.GetUserAccessAsync(
                It.IsAny<string>(), documentId.ToString("D"), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot
            {
                UserId = "caller",
                ResourceId = documentId.ToString("D"),
                AccessRights = AccessRights.Read,
            });
        return accessSource;
    }
}
