using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Tests.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

/// <summary>
/// Contract coverage for <c>POST /api/documents/resolve-email-identity</c> (spaarkeai-word-add-in-r1 task 120, UAT
/// round 12 O6) — the email sibling of <c>POST /api/documents/resolve-identity</c>: is the email open in Outlook
/// already saved to Spaarke, and to which record?
/// </summary>
/// <remarks>
/// <para><b>Scope.</b> <see cref="DocumentEmailIdentityResolutionTests"/> pins the input rules and the query shape at
/// the unit level. THIS file proves the full pipeline — real routing, the resolution filter, then
/// <c>DocumentAuthorizationFilter("read")</c> on the id it resolved, real authentication, real JSON — for each status
/// the route can answer: 200 resolved (filed / unfiled), 200 <c>not_saved</c>, 403 with no metadata, 400 without
/// echoing the key, 401, 503.</para>
/// <para><b>Fixture.</b> Reuses <see cref="DocumentIdentityTestWebAppFactory"/> as-is (CLAUDE.md §11). The route never
/// touches SharePoint Embedded, so its <c>SpeFileStore</c> double is a placeholder the route never calls.</para>
/// </remarks>
public class DocumentEmailIdentityContractTests
{
    private const string Route = "/api/documents/resolve-email-identity";
    private const string MessageId = "<CAF0a1b2c3@mail.example.com>";
    private const string ItemId = "AAMkADAxZWI4YzM4LWVjNTgtNDcwOC1iY2EzLTI1MTc1MGU1MmFhMQBGAAAAAAD/abc+def=";

    // ── Saved, and the caller may read it ─────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveEmail_ForASavedCopyFiledToAMatter_Returns200WithTheSameShapeAsResolveIdentity()
    {
        var documentId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var entityService = SavedCopy(
            DocumentEmailIdentityResolutionTests.Row(
                documentId, ("sprk_matter", new EntityReference("sprk_matter", matterId) { Name = "MAT-000042" })));
        entityService
            .Setup(e => e.RetrieveAsync(
                "sprk_matter", matterId, It.Is<string[]>(c => c.Length == 1 && c[0] == "sprk_mattername"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", matterId) { ["sprk_mattername"] = "Acme v Globex" });

        using var factory = Factory(entityService, AccessSource(documentId, AccessRights.Read));
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(
            Route, new { internetMessageId = MessageId, exchangeItemId = ItemId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("resolved").GetBoolean().Should().BeTrue();
        body.GetProperty("documentId").GetString().Should().Be(documentId.ToString("D"));
        body.GetProperty("documentName").GetString().Should().Be("RE: Discovery schedule");
        body.GetProperty("fileName").GetString().Should().Be("RE_ Discovery schedule_a1b2.eml");
        body.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        var related = body.GetProperty("relatedRecord");
        related.GetProperty("entityType").GetString().Should().Be("sprk_matter");
        related.GetProperty("id").GetString().Should().Be(matterId.ToString("D"));
        related.GetProperty("name").GetString().Should().Be("MAT-000042");
        related.GetProperty("displayName").GetString().Should().Be("Acme v Globex");
        related.GetProperty("number").GetString().Should().Be("MAT-000042");
    }

    [Fact]
    public async Task ResolveEmail_ForAnUnfiledSavedCopy_Returns200WithANullRelatedRecord()
    {
        var documentId = Guid.NewGuid();

        using var factory = Factory(
            SavedCopy(DocumentEmailIdentityResolutionTests.Row(documentId)), AccessSource(documentId, AccessRights.Read));
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("resolved").GetBoolean().Should().BeTrue();
        body.GetProperty("documentId").GetString().Should().Be(documentId.ToString("D"));
        body.GetProperty("relatedRecord").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── Not saved ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveEmail_WhenNoSavedCopyExists_Returns200NotSaved_AndNeverAuthorizes()
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        // STRICT and EMPTY: there is nothing to authorize, so the access source must never be asked.
        var accessSource = new Mock<IAccessDataSource>(MockBehavior.Strict);

        using var factory = Factory(entityService, accessSource);
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(
            Route, new { internetMessageId = MessageId, exchangeItemId = ItemId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("resolved").GetBoolean().Should().BeFalse();
        body.GetProperty("reason").GetString().Should().Be("not_saved");
        body.GetProperty("documentId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── Saved, but the caller may not read the newest copy ────────────────────────────────────

    [Fact]
    public async Task ResolveEmail_WhenCallerLacksReadOnTheNewestCopy_Returns403_LeaksNoMetadata_AndReadsNoMore()
    {
        var documentId = Guid.NewGuid();
        // Only the lookup is set up (STRICT): no row read, no related-record read — the handler never runs.
        var entityService = SavedCopy(DocumentEmailIdentityResolutionTests.Row(
            documentId, ("sprk_matter", new EntityReference("sprk_matter", Guid.NewGuid()) { Name = "MAT-000042" })));

        using var factory = Factory(entityService, AccessSource(documentId, AccessRights.None));
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("Discovery schedule");
        raw.Should().NotContain("MAT-000042");
        raw.Should().NotContain(documentId.ToString("D"));
        entityService.Verify(
            e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Once,
            "one candidate, one authorization — an older copy is never searched");
    }

    // ── Bad input, no credentials, outage ─────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveEmail_WithoutAMessageId_Returns400_AndNeverReadsDataverse()
    {
        using var factory = Factory(
            new Mock<IGenericEntityService>(MockBehavior.Strict), new Mock<IAccessDataSource>(MockBehavior.Strict));
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { exchangeItemId = ItemId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().NotContain(ItemId);
    }

    [Fact]
    public async Task ResolveEmail_WithAnOverLongMessageId_Returns400_WithoutEchoingIt()
    {
        var tooLong = "<" + new string('q', 1200) + "@example.com>";

        using var factory = Factory(
            new Mock<IGenericEntityService>(MockBehavior.Strict), new Mock<IAccessDataSource>(MockBehavior.Strict));
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = tooLong });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("qqqqqqqqqq");
    }

    [Fact]
    public async Task ResolveEmail_WhenUnauthenticated_Returns401()
    {
        using var factory = Factory(
            new Mock<IGenericEntityService>(MockBehavior.Strict), new Mock<IAccessDataSource>(MockBehavior.Strict));
        var response = await factory.CreateClient().PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResolveEmail_WhenDataverseCannotAnswer_Returns503_NeverNotSaved()
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("The request channel timed out while waiting for a reply."));

        using var factory = Factory(entityService, new Mock<IAccessDataSource>(MockBehavior.Strict));
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().NotContain(MessageId);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static DocumentIdentityTestWebAppFactory Factory(
        Mock<IGenericEntityService> entityService, Mock<IAccessDataSource> accessSource)
        => new(
            // Placeholder: this route never reaches SharePoint Embedded.
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor("unused-drive", "unused-item")),
            entityService,
            accessSource);

    private static HttpClient AuthenticatedClient(DocumentIdentityTestWebAppFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }

    /// <summary>The saved-email lookup returns <paramref name="row"/>; nothing else is set up (STRICT).</summary>
    private static Mock<IGenericEntityService> SavedCopy(Entity row)
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_document"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { row }));
        return entityService;
    }

    private static Mock<IAccessDataSource> AccessSource(Guid documentId, AccessRights rights)
    {
        var accessSource = new Mock<IAccessDataSource>();
        accessSource
            .Setup(a => a.GetUserAccessAsync(
                It.IsAny<string>(), documentId.ToString("D"), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot
            {
                UserId = "caller",
                ResourceId = documentId.ToString("D"),
                AccessRights = rights,
            });
        return accessSource;
    }
}
