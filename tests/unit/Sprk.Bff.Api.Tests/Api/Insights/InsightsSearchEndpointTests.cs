using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Insights;

/// <summary>
/// Integration tests for the Wave E task 040 public endpoint
/// <c>POST /api/insights/search</c> (D-P15-06 / FR-04 / SC-04).
/// </summary>
/// <remarks>
/// <para>
/// <b>What's tested in-process</b>: the full Minimal API pipeline — auth
/// (<c>RequireAuthorization()</c>), rate-limit policy (<c>ai-context</c>),
/// validation + ADR-019 ProblemDetails error shapes, success 200 with
/// <see cref="InsightsSearchResponse"/> envelope (citations + summary),
/// empty-result 200 with empty summary (no fabrication), 503 kill-switch path
/// via <see cref="FeatureDisabledException"/> ("ai.rag.disabled"),
/// observability headers (<c>X-Insights-Elapsed-Ms</c>, <c>X-Insights-Hit-Count</c>),
/// and the §3.5 facade contract (only <see cref="IInsightsAi"/> is invoked).
/// <see cref="IInsightsAi"/> is mocked so the in-process suite runs without a real
/// RAG search or LLM call.
/// </para>
/// <para>
/// <b>Why this file lives in <c>tests/unit/Sprk.Bff.Api.Tests/</c></b>: same rationale
/// as the sibling <see cref="InsightEndpointsTests"/> file — the integration project
/// has pre-existing compile errors that block the unit-project test pattern. The unit
/// project hosts WebApplicationFactory-based endpoint tests by convention.
/// </para>
/// <para>
/// <b>Coverage by task POML acceptance criterion</b>:
/// <list type="number">
///   <item>Auth filter applied → <see cref="PostSearch_Unauthenticated_Returns401"/></item>
///   <item>Response carries ranked Observations/Precedents → <see cref="PostSearch_ValidRequest_ReturnsRankedResults"/></item>
///   <item>Synthesis includes grounded [n] citations matching results → asserted in the same success test (Summary is forwarded verbatim from the mocked facade)</item>
///   <item>§3.5 grep (out-of-process verification) — see <c>InsightsSearchEndpoint.cs</c> imports</item>
///   <item>Kill-switch → 503 with errorCode=ai.rag.disabled → <see cref="PostSearch_FeatureDisabled_Returns503ProblemDetails"/></item>
///   <item>spec.md FR-04 + SC-04 (live Dev assertions) covered by smoke after deploy</item>
///   <item>Subject filter respects matter/project/invoice → <see cref="PostSearch_ProjectSubject_ForwardsCorrectScheme"/> + theory</item>
/// </list>
/// </para>
/// </remarks>
public class InsightsSearchEndpointTests : IClassFixture<InsightsSearchEndpointTestFixture>
{
    private readonly InsightsSearchEndpointTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private const string SampleMatterSubject = "matter:11111111-2222-3333-4444-555555555555";
    private const string SampleProjectSubject = "project:11111111-2222-3333-4444-555555555555";
    private const string SampleInvoiceSubject = "invoice:11111111-2222-3333-4444-555555555555";

    public InsightsSearchEndpointTests(InsightsSearchEndpointTestFixture fixture)
    {
        _fixture = fixture;
    }

    // -------------------------------------------------------------------------
    // SUCCESS — POST returns 200 + ranked results + grounded summary
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostSearch_ValidRequest_ReturnsRankedResults()
    {
        // Arrange — facade returns 3 hits + a grounded summary with [1][2][3] citations
        var facadeResult = new InsightsSearchFacadeResult
        {
            Query = "What is the predicted cost?",
            Results = new[]
            {
                new InsightsSearchHit("chunk-1", "doc-A", "Acme APA.pdf", "Predicted cost $280k", "predictedCost", 0.91),
                new InsightsSearchHit("chunk-2", "doc-B", "Beta APA.pdf", "Similar matter cost $310k", "predictedCost", 0.85),
                new InsightsSearchHit("chunk-3", null,    "Cohort.pdf",   "Average $295k across 12 matters", "predictedCost", 0.78)
            },
            Summary = "Based on comparable matters, predicted cost is ~$280k [1], with cohort average $295k [3].",
            DurationMs = 612
        };
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(facadeResult);

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new
        {
            query = "What is the predicted cost?",
            subject = SampleMatterSubject,
            top = 5
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<InsightsSearchResponse>(_jsonOptions);
        body.Should().NotBeNull();
        body!.Results.Should().HaveCount(3, "facade returned 3 hits");
        body.Results[0].ChunkId.Should().Be("chunk-1");
        body.Results[0].Predicate.Should().Be("predictedCost");
        body.Summary.Should().Contain("[1]").And.Contain("[3]",
            "grounded citations [n] must round-trip from facade to wire");

        // Observability headers
        response.Headers.GetValues("X-Insights-Elapsed-Ms").Should().ContainSingle().Which.Should().Be("612");
        response.Headers.GetValues("X-Insights-Hit-Count").Should().ContainSingle().Which.Should().Be("3");

        // Verify the facade was called with correctly parsed subject + tenant + caller
        _fixture.InsightsAiMock.Verify(s => s.SearchAsync(
            It.Is<InsightsSearchFacadeRequest>(r =>
                r.Query == "What is the predicted cost?"
                && r.ParentEntityType == "matter"
                && r.ParentEntityId == "11111111-2222-3333-4444-555555555555"
                && r.TopK == 5
                && r.TenantId == InsightsSearchEndpointTestFixture.TestTenantId
                && r.CallerPrincipal != null),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PostSearch_EmptyResults_Returns200WithEmptySummary()
    {
        // Arrange — facade returns zero hits (no grounding → no fabricated summary)
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InsightsSearchFacadeResult
            {
                Query = "obscure query",
                Results = Array.Empty<InsightsSearchHit>(),
                Summary = string.Empty,
                DurationMs = 84
            });

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "obscure query", subject = SampleMatterSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "no hits is a successful 'no matches' response, not an error");
        var body = await response.Content.ReadFromJsonAsync<InsightsSearchResponse>(_jsonOptions);
        body!.Results.Should().BeEmpty();
        body.Summary.Should().BeEmpty("orchestrator must NOT fabricate a summary without grounding");
        response.Headers.GetValues("X-Insights-Hit-Count").Should().ContainSingle().Which.Should().Be("0");
    }

    [Theory]
    [InlineData(SampleMatterSubject, "matter")]
    [InlineData(SampleProjectSubject, "project")]
    [InlineData(SampleInvoiceSubject, "invoice")]
    public async Task PostSearch_SubjectScheme_ForwardsCorrectEntityType(string subject, string expectedEntityType)
    {
        // Arrange — facade returns empty so we can focus on the forwarded request shape
        InsightsSearchFacadeRequest? captured = null;
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InsightsSearchFacadeRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new InsightsSearchFacadeResult { Query = "q", Results = [], Summary = "", DurationMs = 1 });

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "test", subject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.ParentEntityType.Should().Be(expectedEntityType,
            $"Subject parser must extract scheme '{expectedEntityType}' from '{subject}'");
    }

    [Fact]
    public async Task PostSearch_WithFilter_ForwardsArtifactTypeAndPredicate()
    {
        // Arrange — verify the filter object propagates through to the facade
        InsightsSearchFacadeRequest? captured = null;
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InsightsSearchFacadeRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new InsightsSearchFacadeResult { Query = "q", Results = [], Summary = "", DurationMs = 1 });

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new
        {
            query = "find indemnity clauses",
            subject = SampleMatterSubject,
            filter = new { artifactType = "contract", predicate = "indemnityClause" }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.ArtifactType.Should().Be("contract");
        captured.Predicate.Should().Be("indemnityClause");
    }

    [Fact]
    public async Task PostSearch_TopOverMax_IsClampedTo20()
    {
        // Arrange
        InsightsSearchFacadeRequest? captured = null;
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InsightsSearchFacadeRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new InsightsSearchFacadeResult { Query = "q", Results = [], Summary = "", DurationMs = 1 });

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "x", subject = SampleMatterSubject, top = 999 };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.TopK.Should().Be(20, "topK must be clamped to MaxTopK=20 to bound RAG cost");
    }

    [Fact]
    public async Task PostSearch_NoTopSpecified_DefaultsTo10()
    {
        // Arrange
        InsightsSearchFacadeRequest? captured = null;
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InsightsSearchFacadeRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new InsightsSearchFacadeResult { Query = "q", Results = [], Summary = "", DurationMs = 1 });

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "x", subject = SampleMatterSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.TopK.Should().Be(10, "default TopK is 10 per FR-04");
    }

    // -------------------------------------------------------------------------
    // AUTH — 401 ProblemDetails per ADR-008 + ADR-019
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostSearch_Unauthenticated_Returns401()
    {
        var client = _fixture.CreateUnauthenticatedTenantClient();
        var request = new { query = "q", subject = SampleMatterSubject };

        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "unauthenticated callers get 401 per ADR-008 / RequireAuthorization");
    }

    [Fact]
    public async Task PostSearch_MissingTenantClaim_Returns401WithProblemDetails()
    {
        var client = _fixture.CreateAuthenticatedClientWithoutTenantClaim();
        var request = new { query = "q", subject = SampleMatterSubject };

        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should()
            .Be("application/problem+json", "ADR-019 ProblemDetails for all errors");
    }

    // -------------------------------------------------------------------------
    // VALIDATION — 400 ProblemDetails per ADR-019
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostSearch_EmptyBody_Returns400()
    {
        var client = _fixture.CreateAuthenticatedTenantClient();
        var response = await client.PostAsync("/api/insights/search",
            new StringContent("null", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostSearch_MissingQuery_Returns400()
    {
        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = (string?)null, subject = SampleMatterSubject };

        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task PostSearch_MissingSubject_Returns400()
    {
        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = "" };

        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "subject is required");
    }

    [Fact]
    public async Task PostSearch_UnknownSubjectScheme_Returns400()
    {
        // Arrange — 'document:' is not in SubjectSchemeCatalogOptions.DefaultSchemes
        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = "document:abc-123" };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "subject parser rejects schemes not registered in the catalog");
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().ContainAny("document", "scheme", "subject");
    }

    [Fact]
    public async Task PostSearch_NonGuidSubjectId_Returns400()
    {
        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = "matter:not-a-guid" };

        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "subject parser requires a valid GUID after the scheme");
    }

    // -------------------------------------------------------------------------
    // KILL-SWITCH — 503 via FeatureDisabledException (ADR-032 P3)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostSearch_FeatureDisabled_Returns503ProblemDetails()
    {
        // Arrange — NullRagService surfaces FeatureDisabledException through the facade
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureDisabledException(
                "ai.rag.disabled",
                "RAG services require Analysis:Enabled=true, DocumentIntelligence:Enabled=true, and configured AI Search keys."));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = SampleMatterSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        // Assert — caught FIRST (before generic Exception) and converted to 503
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "ADR-032 P3 surface from NullRagService converts to 503");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("ai.rag.disabled",
            "errorCode extension carries the stable feature-key identifier per ADR-018/ADR-032");
        raw.Should().Contain("https://errors.spaarke.com/feature-disabled",
            "stable type URI per AsFeatureDisabled503 helper");
    }

    // -------------------------------------------------------------------------
    // INTERNAL ERROR — facade exception surfaces as 500 ProblemDetails
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostSearch_FacadeThrows_Returns500ProblemDetails()
    {
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated upstream failure"));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = SampleMatterSubject };

        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("simulated upstream failure",
            "ADR-019: never leak internal exception details to the wire");
        raw.Should().Contain("INSIGHTS_SEARCH_INTERNAL_ERROR",
            "stable error code per ADR-019 helper convention");
    }

    [Fact]
    public async Task PostSearch_FacadeThrowsArgumentException_Returns400ProblemDetails()
    {
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("facade-rejected parameter shape"));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = SampleMatterSubject };

        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "facade ArgumentException maps to 400 per the handler contract");
    }

    // -------------------------------------------------------------------------
    // FORCE-MODE (Wave E2 / FR-05 forward-compat plumbing)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostSearch_ForceModeRag_ProceedsAsRagDispatch()
    {
        // Arrange — forceMode=rag on the /search endpoint is internally consistent
        // (it IS the canonical RAG dispatcher).
        InsightsSearchFacadeRequest? captured = null;
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InsightsSearchFacadeRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new InsightsSearchFacadeResult { Query = "q", Results = [], Summary = "", DurationMs = 1 });

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = SampleMatterSubject, forceMode = "rag" };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "forceMode=rag on the RAG endpoint is consistent — endpoint proceeds normally");
        captured!.ForceMode.Should().Be("rag",
            "ForceMode must be forwarded to the facade for E3 Assistant observability");
    }

    [Fact]
    public async Task PostSearch_ForceModePlaybook_Returns400()
    {
        // Arrange — forceMode=playbook on /search is a wrong-endpoint mismatch
        _fixture.InsightsAiMock.Reset();
        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = SampleMatterSubject, forceMode = "playbook" };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "forceMode=playbook on the RAG endpoint must be rejected — caller should switch endpoints");
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("/api/insights/ask",
            "error message must direct caller to the correct endpoint");
    }

    [Fact]
    public async Task PostSearch_ForceModeUnknown_Returns400()
    {
        // Arrange — invalid forceMode
        _fixture.InsightsAiMock.Reset();
        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = SampleMatterSubject, forceMode = "xyz" };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "forceMode must be one of 'playbook' | 'rag' | null");
    }

    [Fact]
    public async Task PostSearch_NoForceMode_DefaultsToNull()
    {
        // Arrange — verify omitting forceMode produces a null ForceMode on the facade DTO
        InsightsSearchFacadeRequest? captured = null;
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InsightsSearchFacadeRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new InsightsSearchFacadeResult { Query = "q", Results = [], Summary = "", DurationMs = 1 });

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = SampleMatterSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.ForceMode.Should().BeNull(
            "omitted forceMode must propagate as null through the facade — E3 default dispatch");
    }

    // -------------------------------------------------------------------------
    // REGISTRATION
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostSearch_EndpointRegistered()
    {
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InsightsSearchFacadeResult { Query = "x", Results = [], Summary = "", DurationMs = 1 });

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "x", subject = SampleMatterSubject };

        var response = await client.PostAsJsonAsync("/api/insights/search", request);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "MapInsightsSearchEndpoint() registered in EndpointMappingExtensions.cs");
    }
}

/// <summary>
/// Test fixture for <see cref="InsightsSearchEndpointTests"/>. Hosts the BFF API
/// in-process via <see cref="WebApplicationFactory{TEntryPoint}"/>, replaces
/// <see cref="IInsightsAi"/> with a mock so each test asserts against deterministic
/// facade behavior, and provides clients with / without a <c>tid</c> claim so the
/// handler's tid extraction can be exercised.
/// </summary>
/// <remarks>
/// Mirrors the <c>InsightEndpointsTestFixture</c> pattern from the sibling
/// <c>InsightEndpointsTests</c> file to keep auth + bootstrap conventions consistent
/// across the Insights endpoint suite. The duplication is deliberate per the existing
/// convention — each endpoint fixture is independent so test runs are isolated.
/// </remarks>
public class InsightsSearchEndpointTestFixture : WebApplicationFactory<Program>
{
    public Mock<IInsightsAi> InsightsAiMock { get; } = new(MockBehavior.Loose);

    public const string TestTenantId = "00000000-0000-0000-0000-000000000def";
    public const string TestUserOid = "test-user-00000000-0000-0000-0000-insights000040";
    public const string TestBearerToken = "insights-search-test-token";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            // Mirrors InsightEndpointsTestFixture baseline so production option validators
            // pass. Keep these in sync if production option validation changes.
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:UseManagedIdentity"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["Analysis:Enabled"] = "true",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                // spaarke-redis-cache-remediation-r1 task 003 (FR-02): opt into in-memory fallback for tests.
                ["Redis:AllowInMemoryFallback"] = "true",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",
                ["CosmosPersistence:Endpoint"] = "https://test-cosmos.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
                ["ModelSelector:IntentClassification"] = "gpt-4o-mini",
                ["ModelSelector:PlanGeneration"] = "o1-mini",
                ["ModelSelector:NodeGeneration"] = "gpt-4o",
                ["ModelSelector:ClarificationGeneration"] = "gpt-4o-mini",
                ["ModelSelector:AnalysisGeneration"] = "gpt-4o",
                ["ModelSelector:ExtractionGeneration"] = "gpt-4o-mini",
                ["ModelSelector:EmbeddingGeneration"] = "text-embedding-3-large",
                ["ModelSelector:FallbackGeneration"] = "gpt-4o",
                ["PowerBi:TenantId"] = "test-powerbi-tenant-id",
                ["PowerBi:ClientId"] = "test-powerbi-client-id",
                ["PowerBi:ClientSecret"] = "test-powerbi-client-secret",
                ["PowerBi:ApiUrl"] = "https://api.powerbi.com",
                ["PowerBi:Scope"] = "https://analysis.windows.net/.default",
                ["Reporting:ModuleEnabled"] = "false"
            };
            config.AddInMemoryCollection(dict);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // spaarke-redis-cache-remediation-r1 task 003 (FR-02): switch to Development for in-memory
        // cache fallback; disable ValidateScopes to preserve pre-existing test behavior.
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IInsightsAi>();
            services.AddSingleton(InsightsAiMock.Object);

            services.RemoveAll<IHostedService>();

            var dataverseMock = new Mock<IDataverseService>();
            dataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseMock.Object);
        });
    }

    public HttpClient CreateAuthenticatedTenantClient() => CreateClientWithTid(includeTid: true, includeAuth: true);
    public HttpClient CreateAuthenticatedClientWithoutTenantClaim() => CreateClientWithTid(includeTid: false, includeAuth: true);
    public HttpClient CreateUnauthenticatedTenantClient() => CreateClientWithTid(includeTid: true, includeAuth: false);

    private HttpClient CreateClientWithTid(bool includeTid, bool includeAuth)
    {
        var factory = this.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new InsightsSearchAuthOptions(includeTid));

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = InsightsSearchTenantFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = InsightsSearchTenantFakeAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, InsightsSearchTenantFakeAuthHandler>(
                    InsightsSearchTenantFakeAuthHandler.SchemeName, _ => { });

                services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = InsightsSearchTenantFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = InsightsSearchTenantFakeAuthHandler.SchemeName;
                });
            });
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        if (includeAuth)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", TestBearerToken);
        }

        return client;
    }
}

internal sealed class InsightsSearchAuthOptions
{
    public bool IncludeTid { get; }
    public InsightsSearchAuthOptions(bool includeTid) => IncludeTid = includeTid;
}

internal sealed class InsightsSearchTenantFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "InsightsSearchTenantFakeAuth";
    private readonly InsightsSearchAuthOptions _opts;

    public InsightsSearchTenantFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        InsightsSearchAuthOptions opts)
        : base(options, logger, encoder)
    {
        _opts = opts;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));

        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
            return Task.FromResult(AuthenticateResult.Fail("Empty Authorization header"));

        var claims = new List<Claim>
        {
            new("oid", InsightsSearchEndpointTestFixture.TestUserOid),
            new(ClaimTypes.NameIdentifier, InsightsSearchEndpointTestFixture.TestUserOid),
            new(ClaimTypes.Name, "Insights Search Test User"),
            new("name", "Insights Search Test User"),
        };

        if (_opts.IncludeTid)
        {
            claims.Add(new Claim("tid", InsightsSearchEndpointTestFixture.TestTenantId));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
