using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

/// <summary>
/// Contract coverage for <c>GET /api/v1/documents/{id}</c> as the pane-facing read this task
/// (spaarkeai-word-add-in-r1 task 021, FR-07) extends to carry the four AI profile fields
/// (<c>sprk_filesummary</c>/<c>sprk_filetldr</c>/<c>sprk_filekeywords</c>/<c>sprk_documenttype</c>)
/// plus <c>sprk_filesummarystatus</c>.
/// </summary>
/// <remarks>
/// <para><b>Why this route, not a new one (CLAUDE.md §11).</b> Grep evidence (task notes
/// <c>021-profile-section-display.md</c>): <c>GET /api/v1/documents/{id}</c>
/// (<c>DataverseDocumentsEndpoints.cs</c>) already exists, is already keyed by document id, is
/// already gated by <c>AddDocumentAuthorizationFilter("read")</c> +
/// <c>RequireAuthorization()</c> — the exact authorization this task needs — and its response DTO
/// (<c>DocumentEntity</c>) ALREADY declared
/// <c>Summary</c>/<c>Tldr</c>/<c>Keywords</c>/<c>DocumentType</c> properties that no caller of
/// <c>GetDocumentAsync</c> had ever selected or mapped (dead fields). Task 021 wires them up (adds
/// <c>SummaryStatus</c>, extends the <c>ColumnSet</c>, populates the mapper) — zero new BFF routes.
/// The one other Office-facing "document by id" family this task considered,
/// <c>/api/documents/{documentId}/*</c> in <c>FileAccessEndpoints.cs</c>, was rejected precisely
/// because it would have required adding a NEW sibling route, which the POML's escalation trigger
/// gates on a human decision — extending the existing v1 GET avoids that trigger entirely.</para>
/// <para><b>Fixture.</b> <see cref="DocumentProfileTestWebAppFactory"/> inherits
/// <see cref="CustomWebAppFactory"/> and replaces only <see cref="IDocumentDataverseService"/> (the
/// route's one collaborator) and <see cref="IAccessDataSource"/> (the authorization decision, same
/// seam <see cref="Sprk.Bff.Api.Tests.Api.Documents.DocumentIdentityContractTests"/> already
/// established for this exact grant/deny shape). Neither is <c>Mock&lt;HttpMessageHandler&gt;</c>
/// (ADR-038 B1) — both sit at a module boundary the production code already names as a seam.</para>
/// </remarks>
public class DocumentProfileContractTests
{
    private static readonly Guid DocumentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task GetDocument_WithCompletedProfile_Returns200WithAllFourFields()
    {
        var dataverse = new Mock<IDocumentDataverseService>();
        dataverse
            .Setup(d => d.GetDocumentAsync(DocumentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity
            {
                Id = DocumentId.ToString(),
                Name = "Examiner report draft",
                Summary = "A four-paragraph summary of the report.",
                Tldr = "Short TL;DR.",
                Keywords = "litigation, examiner, report",
                DocumentType = "Report",
                SummaryStatus = 100000002, // Completed
            });

        using var factory = new DocumentProfileTestWebAppFactory(dataverse, GrantingAccessSource(DocumentId));
        var client = AuthenticatedClient(factory);

        var response = await client.GetAsync($"/api/v1/documents/{DocumentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        data.GetProperty("summary").GetString().Should().Be("A four-paragraph summary of the report.");
        data.GetProperty("tldr").GetString().Should().Be("Short TL;DR.");
        data.GetProperty("keywords").GetString().Should().Be("litigation, examiner, report");
        data.GetProperty("documentType").GetString().Should().Be("Report");
        data.GetProperty("summaryStatus").GetInt32().Should().Be(100000002);
    }

    [Theory]
    [InlineData(100000000)] // None
    [InlineData(100000001)] // Pending
    [InlineData(100000003)] // OptedOut
    [InlineData(100000004)] // Failed
    [InlineData(100000005)] // NotSupported
    [InlineData(100000006)] // Skipped
    public async Task GetDocument_WithNonCompletedStatus_Returns200WithStatusCodeAndNoProfileText(int statusCode)
    {
        // The six non-Completed states carry the status code through; the pane (not the BFF) maps
        // it to a per-state message. The BFF's job here is only to return the honest column value.
        var dataverse = new Mock<IDocumentDataverseService>();
        dataverse
            .Setup(d => d.GetDocumentAsync(DocumentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity
            {
                Id = DocumentId.ToString(),
                Name = "Untitled draft",
                SummaryStatus = statusCode,
            });

        using var factory = new DocumentProfileTestWebAppFactory(dataverse, GrantingAccessSource(DocumentId));
        var client = AuthenticatedClient(factory);

        var response = await client.GetAsync($"/api/v1/documents/{DocumentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        data.GetProperty("summaryStatus").GetInt32().Should().Be(statusCode);
        data.GetProperty("summary").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetDocument_WhenSummaryStatusNeverSet_ReturnsNullNotAnErrorOrADefault()
    {
        // The column has never been written on this row (Dataverse applies no implicit default).
        // The BFF passes that through as null; the pane treats null the same as None (client-side
        // responsibility, covered by useDocumentProfile unit tests) — the BFF must not invent a value.
        var dataverse = new Mock<IDocumentDataverseService>();
        dataverse
            .Setup(d => d.GetDocumentAsync(DocumentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity { Id = DocumentId.ToString(), Name = "Brand new draft" });

        using var factory = new DocumentProfileTestWebAppFactory(dataverse, GrantingAccessSource(DocumentId));
        var client = AuthenticatedClient(factory);

        var response = await client.GetAsync($"/api/v1/documents/{DocumentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("summaryStatus").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetDocument_WhenUnauthenticated_Returns401()
    {
        var dataverse = new Mock<IDocumentDataverseService>(MockBehavior.Strict);
        using var factory = new DocumentProfileTestWebAppFactory(dataverse, new Mock<IAccessDataSource>(MockBehavior.Strict));
        // No Authorization header — FakeAuthHandler fails authentication and RequireAuthorization()
        // 401s before either the authorization filter or the handler runs.
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/documents/{DocumentId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDocument_WhenCallerLacksReadOnTheDocument_Returns403AndLeaksNoProfileData()
    {
        var dataverse = new Mock<IDocumentDataverseService>(MockBehavior.Strict);
        // Never reached: DocumentAuthorizationFilter denies before the handler calls GetDocumentAsync.

        var accessSource = new Mock<IAccessDataSource>();
        accessSource
            .Setup(a => a.GetUserAccessAsync(
                It.IsAny<string>(), DocumentId.ToString("D"), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot
            {
                UserId = "caller",
                ResourceId = DocumentId.ToString("D"),
                AccessRights = AccessRights.None,
            });

        using var factory = new DocumentProfileTestWebAppFactory(dataverse, accessSource);
        var client = AuthenticatedClient(factory);

        var response = await client.GetAsync($"/api/v1/documents/{DocumentId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("sprk_filesummary");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("data", out _).Should().BeFalse();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static HttpClient AuthenticatedClient(DocumentProfileTestWebAppFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
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

/// <summary>
/// Layers the two module-boundary doubles <c>GET /api/v1/documents/{id}</c> needs on top of
/// <see cref="CustomWebAppFactory"/>'s general BFF test host (FakeAuthHandler, baseline boot
/// config, stub token credential — everything else already handled there).
/// </summary>
internal sealed class DocumentProfileTestWebAppFactory : CustomWebAppFactory
{
    private readonly Mock<IDocumentDataverseService> _dataverseMock;
    private readonly Mock<IAccessDataSource> _accessDataSourceMock;

    public DocumentProfileTestWebAppFactory(
        Mock<IDocumentDataverseService> dataverseMock,
        Mock<IAccessDataSource> accessDataSourceMock)
    {
        _dataverseMock = dataverseMock;
        _accessDataSourceMock = accessDataSourceMock;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDocumentDataverseService>();
            services.AddSingleton(_dataverseMock.Object);

            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton(_accessDataSourceMock.Object);
        });
    }
}
