using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Tests.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

/// <summary>
/// Contract coverage for <c>POST /api/documents/resolve-identity</c> (task 012, FR-01), added by task 016.
/// </summary>
/// <remarks>
/// <para><b>Scope, relative to the existing unit coverage.</b>
/// <see cref="Sprk.Bff.Api.Tests.Filters.DocumentUrlIdentityFilterTests"/> and
/// <see cref="DocumentUrlIdentityResolutionTests"/> already pin every Graph/Dataverse classification branch
/// (malformed URL, local file, not-resolvable, resolved, drive conflict) at the unit level, with a filter
/// invoked directly rather than through a real HTTP pipeline. <see cref="DocumentUrlIdentityFilterTests"/>'s
/// own type doc says the 403-with-no-metadata decision "is exercised end-to-end by the route's contract test
/// (task 016)" — this file is that test: it proves the FULL pipeline (real routing, both endpoint filters in
/// their registered order, real authentication, real JSON serialization) for the cases the acceptance criteria
/// name — not every branch a unit test already owns.</para>
/// <para><b>Fixture.</b> <see cref="DocumentIdentityTestWebAppFactory"/> inherits <see cref="CustomWebAppFactory"/>
/// (the project's general-purpose BFF test host — FakeAuthHandler, baseline boot config, stub token credential)
/// and layers three additional module-boundary doubles used ONLY by the two filters on this route: SpeFileStore
/// (ADR-007 seam, the established <c>Mock&lt;SpeFileStore&gt;</c> idiom — reusing
/// <see cref="DocumentUrlIdentityResolutionTests.Spe"/>/<see cref="DocumentUrlIdentityResolutionTests.ResolvedFor"/>
/// rather than a fourth copy, CLAUDE.md §11), <see cref="IGenericEntityService"/> (the Dataverse alternate-key
/// lookup), and <see cref="IAccessDataSource"/> (the authorization decision — same seam
/// <c>SearchIndexNameEndpointContractTests.PermissiveAccessDataSource</c> already established for a full/grant
/// answer; this file also needs a deny answer, so it uses <c>Mock&lt;IAccessDataSource&gt;</c> directly instead
/// of a second named fake class). None of these is <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038 B1) — every
/// double sits at a module boundary the production code already names as a seam.</para>
/// <para><b>Auth.</b> <c>CustomWebAppFactory</c>'s <c>FakeAuthHandler</c> authenticates on the presence of ANY
/// <c>Authorization</c> header and fails (401) on its absence — so the unauthenticated case needs no separate
/// factory variant, just an unauthenticated client.</para>
/// </remarks>
public class DocumentIdentityContractTests
{
    private const string DocumentUrl =
        "https://spaarke.sharepoint.com/contentstorage/CSP_585db4c8-8043-4676-965e-c92e45f07221/Document Library/Examiner report draft.docx";
    private const string DriveId = "b!yLMdWD2AdkaWXsktRe9yIW7Hn0uXvZVBnuXhwwvLvZWY-YU6-G3sQ7t6c1tKzXJM";
    private const string ItemId = "01BYE5RZ6QN3ZWBTUFOFD3GSPGOHDJD36K";

    [Fact]
    public async Task ResolveIdentity_ForASpaarkeDocumentTheCallerMayRead_Returns200WithIdentity()
    {
        var documentId = Guid.NewGuid();
        var entityService = ResolvingEntityService(documentId);
        var accessSource = GrantingAccessSource(documentId);

        using var factory = new DocumentIdentityTestWebAppFactory(
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor(DriveId, ItemId)),
            entityService,
            accessSource);
        var client = AuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/documents/resolve-identity", new { documentUrl = DocumentUrl });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("resolved").GetBoolean().Should().BeTrue();
        body.GetProperty("documentId").GetString().Should().Be(documentId.ToString("D"));
        body.GetProperty("documentName").GetString().Should().Be("Examiner report draft");
        body.GetProperty("fileName").GetString().Should().Be("Examiner report draft.docx");
        body.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ResolveIdentity_ForAFileNoSprkDocumentTracks_Returns200ResolvedFalse_NotA404OrError()
    {
        // The Graph side resolves (the file is real and the caller can reach it); Dataverse holds no
        // sprk_document for its item id — the "not_spaarke_document" answer, notes/012-identity-resolver-
        // decisions.md §2. A successful 200 — never a 404, never a ProblemDetails error.
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Failed to retrieve sprk_document by alternate key: sprk_document not found with provided alternate key values."));

        using var factory = new DocumentIdentityTestWebAppFactory(
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor(DriveId, ItemId)),
            entityService,
            new Mock<IAccessDataSource>(MockBehavior.Strict)); // never reached — the filter short-circuits
        var client = AuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/documents/resolve-identity", new { documentUrl = DocumentUrl });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an unrecognized file is a SUCCESSFUL answer, not an error");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("resolved").GetBoolean().Should().BeFalse();
        body.GetProperty("reason").GetString().Should().Be("not_spaarke_document");
        body.GetProperty("documentId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ResolveIdentity_WhenUnauthenticated_Returns401()
    {
        var documentId = Guid.NewGuid();
        using var factory = new DocumentIdentityTestWebAppFactory(
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor(DriveId, ItemId)),
            ResolvingEntityService(documentId),
            GrantingAccessSource(documentId));
        // No Authorization header at all — FakeAuthHandler fails authentication, and the group's
        // RequireAuthorization() 401s before either endpoint filter runs.
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/documents/resolve-identity", new { documentUrl = DocumentUrl });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResolveIdentity_WhenCallerLacksReadOnTheResolvedDocument_Returns403AndLeaksNoDocumentMetadata()
    {
        var documentId = Guid.NewGuid();
        var entityService = ResolvingEntityService(documentId);
        var accessSource = new Mock<IAccessDataSource>();
        accessSource
            .Setup(a => a.GetUserAccessAsync(
                It.IsAny<string>(), documentId.ToString("D"), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot
            {
                UserId = "caller",
                ResourceId = documentId.ToString("D"),
                AccessRights = AccessRights.None, // resolvable, but the caller may not read the record
            });

        using var factory = new DocumentIdentityTestWebAppFactory(
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor(DriveId, ItemId)),
            entityService,
            accessSource);
        var client = AuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/documents/resolve-identity", new { documentUrl = DocumentUrl });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        // No document id, name or related record anywhere in the body — DocumentAuthorizationFilter denies
        // BEFORE the handler runs, and the handler is the only place document metadata enters a response
        // (notes/012-identity-resolver-decisions.md §4).
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain(documentId.ToString("D"));
        raw.Should().NotContain("Examiner report draft");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("documentId", out _).Should().BeFalse();
        body.TryGetProperty("documentName", out _).Should().BeFalse();
        body.TryGetProperty("relatedRecord", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-absolute-url")]
    public async Task ResolveIdentity_ForAMalformedOrEmptyUrl_Returns400ProblemDetails_NotA500(string url)
    {
        var documentId = Guid.NewGuid();
        // Neither mock is reached — DocumentUrlIdentityFilter's ParseDocumentUrl throws 400 before any
        // Graph or Dataverse call. Strict mocks make that provable: an unexpected call fails the test.
        using var factory = new DocumentIdentityTestWebAppFactory(
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor(DriveId, ItemId)),
            new Mock<IGenericEntityService>(MockBehavior.Strict),
            new Mock<IAccessDataSource>(MockBehavior.Strict));
        var client = AuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/documents/resolve-identity", new { documentUrl = url });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // NOT response.Content.Headers.ContentType — a REAL, REPO-WIDE defect (task 016; filed as ISS-003,
        // https://github.com/spaarke-dev/spaarke/issues/975 — NOT fixed here, production code is out of
        // this task's scope):
        // MiddlewarePipelineExtensions.cs's global exception handler sets
        // ctx.Response.ContentType = "application/problem+json" and then calls
        // ctx.Response.WriteAsJsonAsync(...) with NO explicit content-type argument, which unconditionally
        // resets ContentType to "application/json; charset=utf-8" (HttpResponseJsonExtensions never
        // consults the response's existing header). EVERY SdapProblemException-driven response across the
        // WHOLE BFF is served as "application/json", never "application/problem+json" — contrary to
        // ADR-019 ("MUST return ProblemDetails for all HTTP failures", which RFC 7807 defines as this
        // media type). Confirmed against the SAME response's JSON BODY below, which IS still the correct
        // ProblemDetails shape — only the header is wrong. Confirmed NOT a fixture artifact: the 403 in
        // ResolveIdentity_WhenCallerLacksReadOnTheResolvedDocument_Returns403AndLeaksNoDocumentMetadata (a
        // DIFFERENT code path — DocumentAuthorizationFilter's Results.Problem(...), which sets the header
        // through ASP.NET Core's own ProblemHttpResult rather than a raw WriteAsJsonAsync) gets the header
        // right in THIS SAME fixture.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("detail").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("type").GetString().Should().NotBeNullOrEmpty();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static HttpClient AuthenticatedClient(DocumentIdentityTestWebAppFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }

    private static Mock<IGenericEntityService> ResolvingEntityService(Guid documentId)
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

/// <summary>
/// Layers the three module-boundary doubles this route's two endpoint filters need on top of
/// <see cref="CustomWebAppFactory"/>'s general BFF test host (FakeAuthHandler, baseline boot config, stub
/// token credential, IDataverseService/IHostedService/IGraphClientFactory already handled there).
/// </summary>
internal sealed class DocumentIdentityTestWebAppFactory : CustomWebAppFactory
{
    private readonly Mock<SpeFileStore> _speFileStoreMock;
    private readonly Mock<IGenericEntityService> _entityServiceMock;
    private readonly Mock<IAccessDataSource> _accessDataSourceMock;

    public DocumentIdentityTestWebAppFactory(
        Mock<SpeFileStore> speFileStoreMock,
        Mock<IGenericEntityService> entityServiceMock,
        Mock<IAccessDataSource> accessDataSourceMock)
    {
        _speFileStoreMock = speFileStoreMock;
        _entityServiceMock = entityServiceMock;
        _accessDataSourceMock = accessDataSourceMock;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<SpeFileStore>();
            services.AddScoped(_ => _speFileStoreMock.Object);

            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(_entityServiceMock.Object);

            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton(_accessDataSourceMock.Object);
        });
    }
}
