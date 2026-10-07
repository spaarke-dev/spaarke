// Regression anchor for GitHub issue #975 (ISS-003, spaarkeai-word-add-in-r1 task 050): the BFF's
// global exception handler (`UseSpaarkeMiddleware` in Infrastructure/DI/MiddlewarePipelineExtensions.cs)
// set `ctx.Response.ContentType = "application/problem+json"` and then called
// `ctx.Response.WriteAsJsonAsync(new {...})` with NO explicit content-type argument.
// `HttpResponseJsonExtensions.WriteAsJsonAsync` unconditionally overwrites `ContentType` with
// "application/json; charset=utf-8" when no content type is passed — it never consults the
// response's existing header. Every `SdapProblemException`-driven error across the WHOLE BFF was
// served as "application/json", contrary to ADR-019 ("MUST return ProblemDetails for all HTTP
// failures", which RFC 7807 defines as media type application/problem+json).
//
// First observed — and deliberately left unasserted, with a long explanatory comment — by task 016's
// DocumentIdentityContractTests.ResolveIdentity_ForAMalformedOrEmptyUrl_Returns400ProblemDetails_NotA500
// (tests/integration/contract/Api/Documents/DocumentIdentityContractTests.cs). That comment is the
// origin of https://github.com/spaarke-dev/spaarke/issues/975. Confirmed NOT a fixture artifact: in
// the SAME fixture, DocumentAuthorizationFilter's 403 (ASP.NET Core's own Results.Problem /
// ProblemHttpResult) already carries the correct header — only the hand-rolled global-handler path
// was wrong.
//
// Fixed here (task 050) by passing the content type explicitly to WriteAsJsonAsync — the framework's
// own mechanism — rather than hand-rolled header juggling. The response BODY is unchanged: same
// anonymous object, same JsonSerializerOptions resolution (WriteAsJsonAsync's convenience overloads
// all delegate to the same 4-arg overload with `options: null`, which is exactly what this fix passes
// explicitly — only `contentType` changes from the implicit `null` to "application/problem+json").
//
// MAINTAIN-class (regression-protector; /test-diet KEEP — tests/integration/regression/** KEEP path
// per ADR-038 §1: "every bug = regression test", naming convention Issue{N}_*Tests.cs). Reuses the
// EXISTING DocumentIdentityTestWebAppFactory fixture and the SAME malformed-URL repro
// DocumentIdentityContractTests already uses to drive DocumentUrlIdentityFilter's
// SdapProblemException(invalid_id, 400) through the full pipeline (CLAUDE.md §11: no new fixture
// class, no new repro mechanism). NO Mock<HttpMessageHandler>, NO DI-registration test, NO ctor-null
// test, NO Stopwatch/Task.Delay.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Tests.Api.Documents;
using Sprk.Bff.Api.Tests.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public class Issue975_ProblemJsonContentTypeTests
{
    private const string DocumentUrl =
        "https://spaarke.sharepoint.com/contentstorage/CSP_585db4c8-8043-4676-965e-c92e45f07221/Document Library/Examiner report draft.docx";
    private const string DriveId = "b!yLMdWD2AdkaWXsktRe9yIW7Hn0uXvZVBnuXhwwvLvZWY-YU6-G3sQ7t6c1tKzXJM";
    private const string ItemId = "01BYE5RZ6QN3ZWBTUFOFD3GSPGOHDJD36K";

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 1. THE DEFECT: an SdapProblemException-driven response must carry application/problem+json.
    //    Before this task's fix, this failed — MediaType was "application/json" instead.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveIdentity_ForAMalformedUrl_SdapProblemExceptionResponse_CarriesProblemJsonContentType()
    {
        // Arrange — DocumentUrlIdentityFilter.ParseDocumentUrl throws SdapProblemException(invalid_id,
        // 400) for a non-absolute URL, uncaught, all the way to UseSpaarkeMiddleware's global handler
        // (the buggy path — see DocumentUrlIdentityFilter.cs line ~50's own comment). Neither mock is
        // reached; strict mocks make that provable.
        using var factory = new DocumentIdentityTestWebAppFactory(
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor(DriveId, ItemId)),
            new Mock<IGenericEntityService>(MockBehavior.Strict),
            new Mock<IAccessDataSource>(MockBehavior.Strict));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/documents/resolve-identity", new { documentUrl = "not-an-absolute-url" });

        // Assert — status unchanged (400); content-type is the ONLY thing this task's fix touches.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json",
            "RFC 7807 (ADR-019) mandates application/problem+json for a ProblemDetails response — " +
            "GitHub #975: UseSpaarkeMiddleware's WriteAsJsonAsync call was overwriting this header " +
            "with application/json before the fix");

        // Body shape UNCHANGED by this fix (byte-for-byte contract, POML constraint) — the same 4
        // fields DocumentIdentityContractTests already pins for this exact route + input.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("detail").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("type").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 2. NEGATIVE CONTROL: a successful (2xx) response's content type is untouched by this fix —
    //    the fix is scoped to the UseExceptionHandler branch only (POML §3 acceptance criterion).
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveIdentity_ForASuccessfulRequest_ContentTypeRemainsApplicationJson()
    {
        var documentId = Guid.NewGuid();
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

        using var factory = new DocumentIdentityTestWebAppFactory(
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor(DriveId, ItemId)),
            entityService,
            accessSource);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/documents/resolve-identity", new { documentUrl = DocumentUrl });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json",
            "a SUCCESSFUL response never goes through UseSpaarkeMiddleware's exception handler — " +
            "only the exception-handler branch changed in this task");
    }
}
