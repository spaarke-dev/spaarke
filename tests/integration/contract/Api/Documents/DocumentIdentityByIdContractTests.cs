using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.ServiceModel;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Tests.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

/// <summary>
/// Contract coverage for <c>GET /api/documents/{documentId}/identity</c> (spaarkeai-word-add-in-r1 task 112, UAT
/// round 11 item 4) — the by-ID sibling of <c>POST /api/documents/resolve-identity</c>, for a document the Word pane
/// knows only by its stamp (e.g. after Quick Save).
/// </summary>
/// <remarks>
/// <para><b>Scope.</b> <see cref="DocumentIdentityByIdResolutionTests"/> pins the Dataverse classification
/// (absent / indeterminate / cancellation) at the unit level. THIS file proves the full pipeline — real routing,
/// <c>DocumentAuthorizationFilter("read")</c> on the route id, real authentication, real JSON — for the cases the
/// task's acceptance criteria name: the same response shape as resolve-identity with the related record for every
/// direct slot type, 403 with no metadata for a caller without read, the documented result for an unknown id, and
/// the 404 / 503 / 400 / 401 edges.</para>
/// <para><b>Fixture.</b> Reuses <see cref="DocumentIdentityTestWebAppFactory"/> as-is (CLAUDE.md §11). This route
/// never touches SharePoint Embedded, so its <c>SpeFileStore</c> double is a placeholder the route never calls.</para>
/// </remarks>
public class DocumentIdentityByIdContractTests
{
    private const int ObjectDoesNotExist = -2147220969; // 0x80040217

    // ── Authorized: same shape as resolve-identity ────────────────────────────────────────────

    /// <summary>
    /// One case per direct slot. The expected (displayName, number) pair encodes the per-type primary-name semantics
    /// (<c>DocumentUrlIdentityResolution.ComplementaryDisplayFieldMap</c>): matter/project's primary name IS the number,
    /// invoice/work-assignment's primary name IS the descriptive name.
    /// </summary>
    public static TheoryData<string, string, string, string, string> SlotCases => new()
    {
        // slot, EntityReference.Name, complementary column, complementary value, expected displayName|number
        { "sprk_matter", "PAT-191111", "sprk_mattername", "Acme v Globex", "Acme v Globex|PAT-191111" },
        { "sprk_project", "PRJ-000417", "sprk_projectname", "Discovery", "Discovery|PRJ-000417" },
        { "sprk_invoice", "March retainer", "sprk_invoicenumber", "INV-000482", "March retainer|INV-000482" },
        { "sprk_workassignment", "Draft response", "sprk_workassignmentnumber", "WA-000031", "Draft response|WA-000031" },
    };

    [Theory]
    [MemberData(nameof(SlotCases))]
    public async Task GetIdentity_ForAReadableDocumentFiledToARecord_Returns200WithNamesAndRelatedRecord(
        string slot, string referenceName, string complementaryColumn, string complementaryValue, string expected)
    {
        var documentId = Guid.NewGuid();
        var relatedId = Guid.NewGuid();
        var entityService = DocumentRow(documentId, (slot, new EntityReference(slot, relatedId) { Name = referenceName }));
        entityService
            .Setup(e => e.RetrieveAsync(
                slot, relatedId, It.Is<string[]>(c => c.Length == 1 && c[0] == complementaryColumn),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(slot, relatedId) { [complementaryColumn] = complementaryValue });

        using var factory = Factory(entityService, GrantingAccessSource(documentId));
        var response = await AuthenticatedClient(factory).GetAsync($"/api/documents/{documentId:D}/identity");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("resolved").GetBoolean().Should().BeTrue();
        body.GetProperty("documentId").GetString().Should().Be(documentId.ToString("D"));
        body.GetProperty("documentName").GetString().Should().Be("Quick-saved brief");
        body.GetProperty("fileName").GetString().Should().Be("Quick-saved brief.docx");
        body.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);

        var related = body.GetProperty("relatedRecord");
        related.GetProperty("entityType").GetString().Should().Be(slot);
        related.GetProperty("id").GetString().Should().Be(relatedId.ToString("D"));
        related.GetProperty("name").GetString().Should().Be(referenceName, "Name stays the primary-name value (task 012 contract)");
        var parts = expected.Split('|');
        related.GetProperty("displayName").GetString().Should().Be(parts[0]);
        related.GetProperty("number").GetString().Should().Be(parts[1]);
    }

    [Fact]
    public async Task GetIdentity_ForAReadableUnfiledDocument_Returns200WithANullRelatedRecord()
    {
        // The Quick Save case the route exists for: known document, no record yet — the pane may offer filing.
        var documentId = Guid.NewGuid();

        using var factory = Factory(DocumentRow(documentId), GrantingAccessSource(documentId));
        var response = await AuthenticatedClient(factory).GetAsync($"/api/documents/{documentId:D}/identity");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("resolved").GetBoolean().Should().BeTrue();
        body.GetProperty("documentId").GetString().Should().Be(documentId.ToString("D"));
        body.GetProperty("relatedRecord").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── Not authorized: 403, nothing leaks ────────────────────────────────────────────────────

    [Fact]
    public async Task GetIdentity_WhenCallerLacksRead_Returns403_LeaksNoMetadata_AndNeverReadsTheRow()
    {
        var documentId = Guid.NewGuid();
        // STRICT and EMPTY: any Dataverse read (the document row, or a related record's display fields) fails the
        // test — proving the filter denies BEFORE the handler, the only place metadata enters a response.
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);

        using var factory = Factory(entityService, DenyingAccessSource(documentId));
        var response = await AuthenticatedClient(factory).GetAsync($"/api/documents/{documentId:D}/identity");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("Quick-saved brief");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("documentId", out _).Should().BeFalse();
        body.TryGetProperty("documentName", out _).Should().BeFalse();
        body.TryGetProperty("fileName", out _).Should().BeFalse();
        body.TryGetProperty("relatedRecord", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetIdentity_ForAnUnknownId_Returns403_IndistinguishableFromNoAccess()
    {
        // Documented result for an unknown id: Dataverse grants no rights on a row that does not exist, so the
        // filter answers exactly as it does for "not yours". The route is not an existence oracle.
        var unknownId = Guid.NewGuid();

        using var factory = Factory(new Mock<IGenericEntityService>(MockBehavior.Strict), DenyingAccessSource(unknownId));
        var response = await AuthenticatedClient(factory).GetAsync($"/api/documents/{unknownId:D}/identity");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetIdentity_WhenUnauthenticated_Returns401()
    {
        var documentId = Guid.NewGuid();

        using var factory = Factory(
            new Mock<IGenericEntityService>(MockBehavior.Strict), new Mock<IAccessDataSource>(MockBehavior.Strict));
        var response = await factory.CreateClient().GetAsync($"/api/documents/{documentId:D}/identity");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Authorized, but the row cannot be read ────────────────────────────────────────────────

    [Fact]
    public async Task GetIdentity_WhenTheRowIsGoneAfterAuthorization_Returns404ProblemDetails()
    {
        var documentId = Guid.NewGuid();
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveAsync("sprk_document", documentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Fault(ObjectDoesNotExist, "sprk_document With Id = ... Does Not Exist"));

        using var factory = Factory(entityService, GrantingAccessSource(documentId));
        var response = await AuthenticatedClient(factory).GetAsync($"/api/documents/{documentId:D}/identity");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetIdentity_WhenDataverseCannotAnswer_Returns503_NeverAnIdentityOr404()
    {
        var documentId = Guid.NewGuid();
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveAsync("sprk_document", documentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("The request channel timed out while waiting for a reply."));

        using var factory = Factory(entityService, GrantingAccessSource(documentId));
        var response = await AuthenticatedClient(factory).GetAsync($"/api/documents/{documentId:D}/identity");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetIdentity_ForANonGuidId_TheCallerIsAllowedOn_Returns400_AndNeverReadsDataverse()
    {
        // The filter decides first (a real data source denies a non-GUID: no row, no rights → 403). Should anything
        // ever allow one, the handler still refuses it with 400 before any Dataverse read.
        using var factory = Factory(new Mock<IGenericEntityService>(MockBehavior.Strict), GrantingAccessSource("not-a-guid"));
        var response = await AuthenticatedClient(factory).GetAsync("/api/documents/not-a-guid/identity");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
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

    private static Mock<IGenericEntityService> DocumentRow(
        Guid documentId, params (string Attribute, EntityReference Reference)[] lookups)
    {
        var row = new Entity("sprk_document", documentId)
        {
            ["sprk_documentname"] = "Quick-saved brief",
            ["sprk_filename"] = "Quick-saved brief.docx",
        };
        foreach (var (attribute, reference) in lookups)
            row[attribute] = reference;

        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveAsync("sprk_document", documentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        return entityService;
    }

    private static Mock<IAccessDataSource> GrantingAccessSource(Guid documentId)
        => AccessSource(documentId.ToString("D"), AccessRights.Read);

    private static Mock<IAccessDataSource> GrantingAccessSource(string resourceId)
        => AccessSource(resourceId, AccessRights.Read);

    private static Mock<IAccessDataSource> DenyingAccessSource(Guid documentId)
        => AccessSource(documentId.ToString("D"), AccessRights.None);

    private static Mock<IAccessDataSource> AccessSource(string resourceId, AccessRights rights)
    {
        var accessSource = new Mock<IAccessDataSource>();
        accessSource
            .Setup(a => a.GetUserAccessAsync(
                It.IsAny<string>(), resourceId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot
            {
                UserId = "caller",
                ResourceId = resourceId,
                AccessRights = rights,
            });
        return accessSource;
    }

    private static FaultException<OrganizationServiceFault> Fault(int errorCode, string message)
        => new(new OrganizationServiceFault { ErrorCode = errorCode, Message = message }, new FaultReason(message));
}
