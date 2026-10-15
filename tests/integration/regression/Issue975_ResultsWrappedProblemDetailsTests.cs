// Regression anchor for GitHub issue #975, fourth defect shape (problem-json-content-type-r1). The
// three earlier #975 fixes (Issue975_ProblemJsonContentTypeTests.cs — the global exception handler;
// Issue975_ChatAttachmentValidationProblemJsonTests.cs and Issue975_OfficeJobStreamProblemJsonTests.cs —
// hand-rolled WriteAsJsonAsync sites) all concerned code that set the header and then lost it. This
// file covers the shape their survey missed:
//
//     return Results.BadRequest(new ProblemDetails { ... });   // also NotFound(...), Conflict(...)
//
// `Results.BadRequest(object?)` is a plain value result (`BadRequest<object>`): it serializes the
// ProblemDetails like any DTO and writes `application/json`. The BODY is an RFC 7807 document but the
// MEDIA TYPE is not, contrary to ADR-019. Fixed by returning `Results.Problem(problemDetails)`
// (ProblemHttpResult), which writes `application/problem+json`. Every converted site sets `Status`
// explicitly, so the status code is unchanged — `Results.Problem(ProblemDetails)` defaults to 500 only
// when Status is null, and the ArchTest ProblemDetailsContentTypeGuardTests keeps the shape from
// coming back.
//
// One case per changed file, each asserting: status UNCHANGED, media type application/problem+json,
// and title / detail / every extension the site sets PRESERVED (`code`, `invalidValues`, `validValues`
// are client contracts — SearchErrorCodes). The only body difference after the fix is ADDITIVE: the
// framework fills `type` with the RFC 9110 section URI for the status, as it already does at every
// other Results.Problem site in the BFF.
//
// Technique per route — reuse the existing fixture for that route, no new host:
//   - JobsEndpoints / MembershipAdminEndpoints: AdminJobsTestFixture / AdminMembershipTestFixture (HTTP).
//   - RagEndpoints: RouteSweepAuthorizationFixture (HTTP, the real MapRagEndpoints + filters).
//   - SemanticSearchEndpoints: SearchIndexNameEndpointTestFixture (HTTP, real filter + handler).
//   - VisualizationEndpoints / RecordSearchEndpoints: their handlers are `internal` precisely so tests
//     drive the shipped code (see their remarks); the returned IResult is EXECUTED against a
//     DefaultHttpContext so the assertion is on the bytes and headers written, not on the result type.
//
// MAINTAIN-class (regression-protector; ADR-038 §1 KEEP path tests/integration/regression/**,
// Issue{N}_*Tests.cs naming). NO Mock<HttpMessageHandler>, NO DI-registration test, NO ctor-null test,
// NO reflection, NO Stopwatch/Task.Delay.

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.Admin.Models;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Visualization;
using Sprk.Bff.Api.Tests.Api.Admin;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.EndToEnd;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>Shared assertions for the Results-wrapped-ProblemDetails regression cases.</summary>
internal static class ProblemJsonAssert
{
    internal const string ProblemJson = "application/problem+json";

    /// <summary>Asserts status, media type, title and detail of a wire response; returns the parsed body.</summary>
    internal static async Task<JsonElement> ProblemAsync(
        HttpResponseMessage response, HttpStatusCode status, string title, string? detailContains = null)
    {
        response.StatusCode.Should().Be(status, "the status code is part of the contract and must not change");
        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be(ProblemJson,
            "ADR-019 / RFC 7807: a ProblemDetails body is served as application/problem+json — " +
            "Results.BadRequest/NotFound/Conflict(new ProblemDetails) served it as application/json (#975)");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        AssertBody(body, (int)status, title, detailContains);
        return body;
    }

    internal static void AssertBody(JsonElement body, int status, string title, string? detailContains)
    {
        body.GetProperty("status").GetInt32().Should().Be(status);
        body.GetProperty("title").GetString().Should().Be(title);
        if (detailContains is not null)
        {
            body.GetProperty("detail").GetString().Should().Contain(detailContains);
        }
    }

    /// <summary>
    /// Executes a handler's <see cref="IResult"/> the way the endpoint pipeline would, and returns what
    /// was written: status, Content-Type header, parsed body.
    /// </summary>
    internal static async Task<(int Status, string? ContentType, JsonElement Body)> ExecuteAsync(
        IResult result, HttpContext httpContext)
    {
        httpContext.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        var buffer = new MemoryStream();
        httpContext.Response.Body = buffer;

        await result.ExecuteAsync(httpContext);

        buffer.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(buffer);
        return (httpContext.Response.StatusCode, httpContext.Response.ContentType, body);
    }
}

// =====================================================================================================
// Api/Admin/JobsEndpoints.cs — Results.NotFound(...) and Results.Conflict(...) sites
// =====================================================================================================

public sealed class Issue975_JobsEndpointsProblemJsonTests : IClassFixture<AdminJobsTestFixture>
{
    private readonly AdminJobsTestFixture _fixture;

    public Issue975_JobsEndpointsProblemJsonTests(AdminJobsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetJobStatus_ForAnUnregisteredJob_Is404ProblemJson()
    {
        using var client = _fixture.CreateAdminClient();
        var jobId = $"issue975-unknown-{Guid.NewGuid():N}";

        var response = await client.GetAsync($"/api/admin/jobs/{jobId}/status");

        await ProblemJsonAssert.ProblemAsync(response, HttpStatusCode.NotFound, "Not Found", jobId);
    }

    [Fact]
    public async Task TriggerJob_WhileTheJobIsAlreadyRunning_Is409ProblemJson()
    {
        using var client = _fixture.CreateAdminClient();
        var jobId = $"issue975-busy-{Guid.NewGuid():N}";
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Registry.Register(new BlockingScheduledJob(jobId, release.Task));

        try
        {
            var first = await client.PostAsync($"/api/admin/jobs/{jobId}/trigger", content: null);
            var overlapping = await client.PostAsync($"/api/admin/jobs/{jobId}/trigger", content: null);

            first.StatusCode.Should().Be(HttpStatusCode.Accepted);
            await ProblemJsonAssert.ProblemAsync(overlapping, HttpStatusCode.Conflict, "Job Already Running", jobId);
        }
        finally
        {
            release.SetResult();
        }
    }

    /// <summary>Runs until <c>release</c> completes, keeping the job "already running" for the 409.</summary>
    private sealed class BlockingScheduledJob(string jobId, Task release) : IScheduledJob
    {
        public string JobId => jobId;
        public string DisplayName => "Issue 975 blocking job";
        public string Description => "Runs until released";

        public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
        {
            await release.WaitAsync(cancellationToken);
            return new JobRunResult(true, null, 1, TimeSpan.Zero);
        }
    }
}

// =====================================================================================================
// Api/Admin/MembershipAdminEndpoints.cs — Results.BadRequest(...) and Results.NotFound(...) sites
// =====================================================================================================

public sealed class Issue975_MembershipAdminProblemJsonTests : IClassFixture<AdminMembershipTestFixture>
{
    private readonly AdminMembershipTestFixture _fixture;

    public Issue975_MembershipAdminProblemJsonTests(AdminMembershipTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.MembershipDiscoveryMock.Reset();
    }

    [Fact]
    public async Task GetDiscovered_ForAnEntityDataverseDoesNotKnow_Is404ProblemJson()
    {
        _fixture.MembershipDiscoveryMock
            .Setup(d => d.DiscoverAsync("issue975_unknown", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Entity 'issue975_unknown' not found in Dataverse metadata."));
        using var client = _fixture.CreateAdminClient();

        var response = await client.GetAsync("/api/admin/membership/discovered/issue975_unknown");

        await ProblemJsonAssert.ProblemAsync(
            response, HttpStatusCode.NotFound, "Not Found", "Entity 'issue975_unknown' not found in Dataverse metadata.");
    }

    [Fact]
    public async Task GetDiscovered_WhenTheServiceRejectsTheEntityType_Is400ProblemJson()
    {
        _fixture.MembershipDiscoveryMock
            .Setup(d => d.DiscoverAsync("issue975_bad", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("issue975 rejected entity type"));
        using var client = _fixture.CreateAdminClient();

        var response = await client.GetAsync("/api/admin/membership/discovered/issue975_bad");

        await ProblemJsonAssert.ProblemAsync(
            response, HttpStatusCode.BadRequest, "Invalid Request", "issue975 rejected entity type");
    }
}

// =====================================================================================================
// Api/Ai/RagEndpoints.cs — Results.BadRequest(...) sites (19 BadRequest + 1 NotFound in this file)
// =====================================================================================================

public sealed class Issue975_RagEndpointsProblemJsonTests : IClassFixture<RouteSweepAuthorizationFixture>
{
    private readonly RouteSweepAuthorizationFixture _fixture;

    public Issue975_RagEndpointsProblemJsonTests(RouteSweepAuthorizationFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetBoundaries();
    }

    [Fact]
    public async Task Search_WithAnEmptyQuery_Is400ProblemJson_AndNoSearchRuns()
    {
        using var client = _fixture.CreateCallerClient();

        var response = await client.PostAsJsonAsync("/api/ai/rag/search", new RagSearchRequest
        {
            Query = "",
            Options = new RagSearchOptions { TenantId = RouteSweepAuthorizationFixture.CallerTenantId, TopK = 10 },
        });

        await ProblemJsonAssert.ProblemAsync(response, HttpStatusCode.BadRequest, "Invalid Request", "Query is required");
        _fixture.Rag.Verify(
            r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

// =====================================================================================================
// Api/Ai/SemanticSearchEndpoints.cs — Results.BadRequest(...) sites carrying `code` + value lists
// =====================================================================================================

public sealed class Issue975_SemanticSearchProblemJsonTests : IClassFixture<SearchIndexNameEndpointTestFixture>
{
    private readonly SearchIndexNameEndpointTestFixture _fixture;

    public Issue975_SemanticSearchProblemJsonTests(SearchIndexNameEndpointTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetCapture();
    }

    [Fact]
    public async Task Search_WithAnUnknownFilterEntityType_Is400ProblemJson_WithEveryExtensionPreserved()
    {
        var client = _fixture.CreateAuthenticatedClient();
        const string bodyJson = """
            {
              "query": "force majeure",
              "scope": "entity",
              "entityType": "matter",
              "entityId": "00000000-0000-0000-0000-0000000000e1",
              "filters": { "entityTypes": ["issue975_bogus"] }
            }
            """;
        using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/ai/search", content);

        var body = await ProblemJsonAssert.ProblemAsync(
            response, HttpStatusCode.BadRequest, "Invalid Entity Types", "'issue975_bogus'");
        body.GetProperty("code").GetString().Should().Be(SearchErrorCodes.InvalidEntityTypes,
            "`code` is the client's switch key (SearchErrorCodes) — it must survive the media-type change");
        body.GetProperty("invalidValues").EnumerateArray().Select(v => v.GetString())
            .Should().Equal("issue975_bogus");
        body.GetProperty("validValues").EnumerateArray().Select(v => v.GetString())
            .Should().Equal(ValidEntityTypes.All);
        _fixture.LastCapturedRequest.Should().BeNull("validation refused the request before the service ran");
    }
}

// =====================================================================================================
// Api/Ai/VisualizationEndpoints.cs and Api/Ai/RecordSearchEndpoints.cs — internal handlers, IResult
// executed against a DefaultHttpContext
// =====================================================================================================

public sealed class Issue975_InternalHandlerProblemJsonTests
{
    private const string TenantId = "aaaaaaaa-0975-0975-0975-aaaaaaaaaaaa";

    private static DefaultHttpContext CallerContext(VisualizationAuthorizationSubject? rowObligation)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("tid", TenantId), new Claim("oid", "cccccccc-0975-0975-0975-cccccccccccc")], "test")),
        };
        httpContext.Request.Headers.Authorization = "Bearer issue975-token";

        if (rowObligation is { } subject)
        {
            httpContext.Items[VisualizationAuthorization.HttpContextItemsKey] = new VisualizationAuthorization
            {
                RequiresPerRowDocumentAuthorization = true,
                Subject = subject,
            };
        }

        return httpContext;
    }

    [Fact]
    public async Task Visualization_Related_WhenTheSourceDocumentIsUnknown_Is404ProblemJson()
    {
        var documentId = Guid.Parse("09750000-0000-0000-0000-000000000404");
        var visualization = new Mock<IVisualizationService>(MockBehavior.Strict);
        visualization
            .Setup(v => v.GetRelatedDocumentsAsync(documentId, It.IsAny<VisualizationOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("no embedding"));
        var httpContext = CallerContext(VisualizationAuthorizationSubject.SourceDocument);

        var result = await VisualizationEndpoints.GetRelatedDocuments(
            documentId,
            new VisualizationQueryParameters(),
            httpContext,
            visualization.Object,
            authorizationService: null!, // never reached: the service throws before rows are authorized
            NullLogger<Program>.Instance,
            CancellationToken.None);
        var (status, contentType, body) = await ProblemJsonAssert.ExecuteAsync(result, httpContext);

        status.Should().Be(StatusCodes.Status404NotFound);
        contentType.Should().StartWith(ProblemJsonAssert.ProblemJson);
        ProblemJsonAssert.AssertBody(body, 404, "Document Not Found", documentId.ToString());
    }

    [Fact]
    public async Task Visualization_RelatedFromContent_WithoutAForm_Is400ProblemJson()
    {
        var httpContext = CallerContext(VisualizationAuthorizationSubject.UploadedContent);
        httpContext.Request.ContentType = "application/json";

        var result = await VisualizationEndpoints.IndexTemporaryContent(
            httpContext,
            visualizationService: null!, // never reached: the content-type check refuses first
            NullLogger<Program>.Instance,
            CancellationToken.None);
        var (status, contentType, body) = await ProblemJsonAssert.ExecuteAsync(result, httpContext);

        status.Should().Be(StatusCodes.Status400BadRequest);
        contentType.Should().StartWith(ProblemJsonAssert.ProblemJson);
        ProblemJsonAssert.AssertBody(body, 400, "Bad Request", "multipart/form-data");
    }

    [Fact]
    public async Task RecordSearch_WithAnEmptyQuery_Is400ProblemJson_WithCodePreserved()
    {
        var httpContext = CallerContext(rowObligation: null);

        var result = await RecordSearchEndpoints.PostRecordSearch(
            new RecordSearchRequest { Query = " ", RecordTypes = [RecordEntityType.Matter] },
            recordSearchService: null!, // never reached: query validation refuses first
            authorizationService: null!,
            httpContext,
            NullLoggerFactory.Instance,
            CancellationToken.None);
        var (status, contentType, body) = await ProblemJsonAssert.ExecuteAsync(result, httpContext);

        status.Should().Be(StatusCodes.Status400BadRequest);
        contentType.Should().StartWith(ProblemJsonAssert.ProblemJson);
        ProblemJsonAssert.AssertBody(body, 400, "Query Required", "query is required");
        body.GetProperty("code").GetString().Should().Be(SearchErrorCodes.QueryRequired);
    }
}
