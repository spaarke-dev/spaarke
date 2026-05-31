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
using Spaarke.Dataverse;
using Moq;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Insights;

/// <summary>
/// Integration tests for the D-P15 public endpoint
/// <c>POST /api/insights/ask</c> (task 061).
/// </summary>
/// <remarks>
/// <para>
/// <b>What's tested in-process</b>: the full Minimal API pipeline — auth
/// (<c>RequireAuthorization()</c>), rate-limit policy (<c>ai-context</c>),
/// validation + ADR-019 ProblemDetails error shapes, success 200 with
/// <see cref="InsightAskResponse"/> envelope (Artifact branch), decline 200
/// with envelope (Decline branch), observability headers
/// (<c>X-Insights-Cache</c>, <c>X-Insights-Elapsed-Ms</c>), and the §3.5
/// facade contract (only <see cref="IInsightsAi"/> is invoked).
/// <see cref="IInsightsAi"/> is mocked so the in-process suite runs without
/// a real playbook execution.
/// </para>
/// <para>
/// <b>Why this file lives in <c>tests/unit/Sprk.Bff.Api.Tests/</c> rather than
/// <c>tests/integration/Spe.Integration.Tests/</c></b> (the path the task 061
/// POML listed): the integration project currently has 4 pre-existing compile
/// errors in <c>ExternalAccess/ExternalAccessIntegrationTests.cs</c> (deliberate
/// <c>InviteExternalUserRequest</c> rewrite per commit <c>40b6633f</c>) that the
/// task-012 fix-up commit <c>1c2a1053</c> repaired only for the unit test
/// project. Placing this file in the unit project lets it run today; the unit
/// project already hosts WebApplicationFactory-based endpoint tests like
/// <c>ChatActionsEndpointTests</c>, <c>HandlerEndpointsTests</c>, etc., so the
/// placement is consistent with existing convention.
/// </para>
/// <para>
/// <b>Coverage by acceptance criterion</b> (task 061 POML):
/// <list type="number">
///   <item>Valid POST → 200 + Artifact OR Decline ✅
///   <see cref="PostAsk_ValidRequest_ReturnsArtifact"/> +
///   <see cref="PostAsk_ValidRequest_ReturnsDecline"/></item>
///   <item>Invalid POST (missing question) → 400 ProblemDetails ✅
///   <see cref="PostAsk_MissingQuestion_Returns400"/></item>
///   <item>Unauthenticated → 401 ✅
///   <see cref="PostAsk_Unauthenticated_Returns401"/></item>
///   <item>Rate-limited → 429 ProblemDetails with Retry-After: verified
///   architecturally by registering the <c>ai-context</c> policy on the
///   endpoint group; the <c>RateLimitingModule.OnRejected</c> handler is
///   shared with other endpoints and sets the header centrally. Not
///   exercised at the per-request level here because triggering 60 actual
///   requests/min in a unit test is brittle and the shared module owns the
///   429+Retry-After contract.</item>
///   <item>§3.5 grep clean — verified externally by the manual grep gate
///   (run separately in the task completion step; not an in-process test).</item>
///   <item>Endpoint registered ✅
///   <see cref="PostAsk_EndpointRegistered"/></item>
/// </list>
/// </para>
/// </remarks>
public class InsightEndpointsTests : IClassFixture<InsightEndpointsTestFixture>
{
    private readonly InsightEndpointsTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid SampleQuestion = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string SampleSubject = "matter:M-1234";

    public InsightEndpointsTests(InsightEndpointsTestFixture fixture)
    {
        _fixture = fixture;
    }

    // -------------------------------------------------------------------------
    // SUCCESS PATH — POST returns 200 + Artifact envelope
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostAsk_ValidRequest_ReturnsArtifact()
    {
        // Arrange — facade returns a success Inference
        var inferenceArtifact = BuildInferenceArtifact();
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsAgentResult.Success(inferenceArtifact, cacheHit: false, processingTimeMs: 123));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new
        {
            question = SampleQuestion.ToString(),
            subject = SampleSubject,
            parameters = new Dictionary<string, string> { ["lookBackYears"] = "3" }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "valid POST returns 200 even on the success path per POML acceptance criterion 1");

        var body = await response.Content.ReadFromJsonAsync<InsightAskResponse>(_jsonOptions);
        body.Should().NotBeNull();
        body!.Artifact.Should().NotBeNull("success path populates Artifact");
        body.Decline.Should().BeNull("success path leaves Decline null");
        body.Artifact!.Subject.Should().Be(SampleSubject);
        body.Artifact.TenantId.Should().Be(InsightEndpointsTestFixture.TestTenantId);

        // Observability headers
        response.Headers.GetValues("X-Insights-Cache").Should().ContainSingle().Which.Should().Be("false");
        response.Headers.GetValues("X-Insights-Elapsed-Ms").Should().ContainSingle().Which.Should().Be("123");

        // Verify the facade was called with the parsed Guid + tenant + scope hash
        _fixture.InsightsAiMock.Verify(s => s.AnswerQuestionAsync(
            It.Is<InsightsAgentRequest>(r =>
                r.Question == SampleQuestion
                && r.Subject == SampleSubject
                && r.TenantId == InsightEndpointsTestFixture.TestTenantId
                && !string.IsNullOrWhiteSpace(r.AccessibleScopeHash)
                && r.Parameters != null
                && r.Parameters["lookBackYears"] == "3"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PostAsk_ValidRequest_ReturnsDecline()
    {
        // Arrange — facade returns a structured decline (D-49)
        var decline = new DeclineResponse
        {
            Reason = "insufficient-evidence",
            Explanation = "Only 4 comparable matters were found; predict-matter-cost requires at least 12.",
            MinimumEvidenceNeeded = new Dictionary<string, object>
            {
                ["comparableMatters"] = new { have = 4, need = 12 }
            },
            SuggestedActions = new[] { "Broaden the matter-type filter from 'IP licensing' to 'IP'" },
            ConfidenceInDecline = 0.92
        };
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsAgentResult.Declined(decline, cacheHit: false, processingTimeMs: 88));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { question = SampleQuestion.ToString(), subject = SampleSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert — decline is NOT an error; 200 OK per the wire-shape decision
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "decline is a successful structured response per D-49, not an error");

        var body = await response.Content.ReadFromJsonAsync<InsightAskResponse>(_jsonOptions);
        body.Should().NotBeNull();
        body!.Decline.Should().NotBeNull("decline path populates Decline");
        body.Artifact.Should().BeNull("decline path leaves Artifact null");
        body.Decline!.Reason.Should().Be("insufficient-evidence");
        body.Decline.ConfidenceInDecline.Should().Be(0.92);

        // Headers still set on decline
        response.Headers.GetValues("X-Insights-Cache").Should().ContainSingle().Which.Should().Be("false");
        response.Headers.GetValues("X-Insights-Elapsed-Ms").Should().ContainSingle().Which.Should().Be("88");
    }

    [Fact]
    public async Task PostAsk_CacheHitTrue_SetsCacheHeader()
    {
        // Arrange
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsAgentResult.Success(BuildInferenceArtifact(), cacheHit: true, processingTimeMs: 5));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { question = SampleQuestion.ToString(), subject = SampleSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Insights-Cache").Should().ContainSingle().Which.Should().Be("true",
            "cache hit propagates from facade through the response header for client-side observability");
    }

    // -------------------------------------------------------------------------
    // AUTH — 401 ProblemDetails per ADR-008 + ADR-019
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostAsk_Unauthenticated_Returns401()
    {
        // Arrange
        var client = _fixture.CreateUnauthenticatedTenantClient();
        var request = new { question = SampleQuestion.ToString(), subject = SampleSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert — RequireAuthorization() rejects before our handler runs
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "unauthenticated callers get 401 per ADR-008 / RequireAuthorization");
    }

    [Fact]
    public async Task PostAsk_MissingTenantClaim_Returns401WithProblemDetails()
    {
        // Arrange — authenticated but token has no 'tid' claim
        var client = _fixture.CreateAuthenticatedClientWithoutTenantClaim();
        var request = new { question = SampleQuestion.ToString(), subject = SampleSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert — handler explicitly checks for tid and returns 401 ProblemDetails
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should()
            .Be("application/problem+json", "ADR-019 ProblemDetails for all errors");
    }

    // -------------------------------------------------------------------------
    // VALIDATION — 400 ProblemDetails per ADR-019
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostAsk_EmptyBody_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedTenantClient();

        // Act — null body
        var response = await client.PostAsync("/api/insights/ask",
            new StringContent("null", System.Text.Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostAsk_MissingQuestion_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { question = (string?)null, subject = SampleSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert — POML acceptance criterion 2
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "question is required per POML acceptance criterion 2");
        response.Content.Headers.ContentType?.MediaType.Should()
            .Be("application/problem+json", "ADR-019 ProblemDetails");
    }

    [Fact]
    public async Task PostAsk_MissingSubject_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { question = SampleQuestion.ToString(), subject = "" };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "subject is required");
    }

    [Fact]
    public async Task PostAsk_NonGuidQuestion_NotInNameMap_Returns400()
    {
        // Arrange — friendly name with empty map (default fixture has no map entries)
        // → name resolution returns Guid.Empty → 400 with registered-names listing.
        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { question = "predict-matter-cost", subject = SampleSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "name not registered in Insights:Playbooks:Map AND not a Guid → 400");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Insights:Playbooks", "error must point operator to the config section that fixes this");
        body.Should().Contain("predict-matter-cost", "error must echo the rejected name to aid debugging");
    }

    [Fact]
    public async Task PostAsk_CanonicalNameInMap_ResolvesToGuid_ReturnsArtifact()
    {
        // Arrange — canonical playbook name resolves via config map to a real Guid.
        // The Guid we configure must match what AnswerQuestionAsync sees so we can assert
        // the resolution actually happened (not just that the path succeeded).
        var configuredGuid = Guid.Parse("63b80630-975b-f111-a825-3833c5d9bcab");
        InsightsAgentRequest? captured = null;

        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InsightsAgentRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(InsightsAgentResult.Success(BuildInferenceArtifact(), cacheHit: false, processingTimeMs: 12));

        var client = _fixture.CreateAuthenticatedTenantClientWithNameMap(new Dictionary<string, Guid>
        {
            ["predict-matter-cost@v1"] = configuredGuid
        });
        var request = new { question = "predict-matter-cost@v1", subject = SampleSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, "canonical name resolved via config map");
        captured.Should().NotBeNull();
        captured!.Question.Should().Be(configuredGuid, "endpoint must pass the resolved Guid, not parse the name as a Guid");
    }

    [Fact]
    public async Task PostAsk_CanonicalNameInMap_LookupIsCaseInsensitive()
    {
        // Arrange — mixed case input MUST resolve to the same Guid as the registered name.
        var configuredGuid = Guid.Parse("63b80630-975b-f111-a825-3833c5d9bcab");
        InsightsAgentRequest? captured = null;

        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InsightsAgentRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(InsightsAgentResult.Success(BuildInferenceArtifact(), cacheHit: false, processingTimeMs: 7));

        var client = _fixture.CreateAuthenticatedTenantClientWithNameMap(new Dictionary<string, Guid>
        {
            ["predict-matter-cost@v1"] = configuredGuid // lower-case registration
        });
        var request = new { question = "PREDICT-MATTER-COST@V1", subject = SampleSubject }; // upper-case lookup

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, "case-insensitive lookup must match");
        captured.Should().NotBeNull();
        captured!.Question.Should().Be(configuredGuid);
    }

    [Fact]
    public async Task PostAsk_GuidQuestion_StillWorks_EvenWithMapConfigured()
    {
        // Arrange — backward compatibility: raw Guid path must continue working when a
        // map is also configured. Guid attempt happens BEFORE map lookup.
        InsightsAgentRequest? captured = null;

        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InsightsAgentRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(InsightsAgentResult.Success(BuildInferenceArtifact(), cacheHit: false, processingTimeMs: 5));

        var directGuid = Guid.NewGuid();
        var client = _fixture.CreateAuthenticatedTenantClientWithNameMap(new Dictionary<string, Guid>
        {
            ["predict-matter-cost@v1"] = Guid.Parse("63b80630-975b-f111-a825-3833c5d9bcab")
        });
        var request = new { question = directGuid.ToString(), subject = SampleSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.Question.Should().Be(directGuid, "raw Guid must take precedence over map lookup");
    }

    [Fact]
    public async Task PostAsk_NonMatterSubject_Returns400()
    {
        // Arrange — Phase 1 contract: only matter: scheme
        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { question = SampleQuestion.ToString(), subject = "document:abc-123" };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "Phase 1 accepts only matter: subject scheme");
    }

    [Fact]
    public async Task PostAsk_EmptyMatterId_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { question = SampleQuestion.ToString(), subject = "matter:" };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "matter: prefix without id is invalid");
    }

    // -------------------------------------------------------------------------
    // INTERNAL ERROR — facade exception surfaces as 500 ProblemDetails
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostAsk_FacadeThrows_Returns500ProblemDetails()
    {
        // Arrange — facade blows up
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated upstream failure"));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { question = SampleQuestion.ToString(), subject = SampleSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert — handler catches and returns 500 ProblemDetails (no content leak per ADR-019)
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should()
            .Be("application/problem+json", "ADR-019 ProblemDetails");

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("simulated upstream failure",
            "ADR-019: never leak internal exception details to the wire");
        raw.Should().Contain("INSIGHTS_INTERNAL_ERROR",
            "stable error code is included per ADR-019 helper convention");
    }

    [Fact]
    public async Task PostAsk_FacadeThrowsArgumentException_Returns400ProblemDetails()
    {
        // Arrange — facade-side validation that slipped past our pre-checks
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("facade-rejected parameter shape"));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { question = SampleQuestion.ToString(), subject = SampleSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert — ArgumentException maps to 400 since it represents validation
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "facade ArgumentException maps to 400 per the handler contract");
    }

    // -------------------------------------------------------------------------
    // REGISTRATION — acceptance criterion 6
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostAsk_EndpointRegistered()
    {
        // Arrange — facade returns success so we just need the route to exist
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsAgentResult.Success(BuildInferenceArtifact(), cacheHit: false, processingTimeMs: 1));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { question = SampleQuestion.ToString(), subject = SampleSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert — anything other than 404 proves the route is registered
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "POML acceptance criterion 6: MapInsightsAskEndpoint() registered in EndpointMappingExtensions.cs");
    }

    // -------------------------------------------------------------------------
    // Test data helpers
    // -------------------------------------------------------------------------

    private static InferenceArtifact BuildInferenceArtifact()
        => new()
        {
            Id = "inf:predict-matter-cost:M-1234",
            Subject = SampleSubject,
            Predicate = "predictedCost",
            Value = new Value
            {
                Raw = JsonDocument.Parse("280000").RootElement,
                DisplayHint = "currency-usd"
            },
            AsOf = DateTimeOffset.UtcNow,
            ProducedBy = new ProducedBy { Kind = "agent", Id = "agent://insights-v1", Version = "v1" },
            Scope = new Scope { TenantId = InsightEndpointsTestFixture.TestTenantId, MatterId = "M-1234" },
            TenantId = InsightEndpointsTestFixture.TestTenantId,
            Confidence = 0.74,
            Reasoning = "Based on 12 comparable matters"
        };
}

/// <summary>
/// Test fixture for <see cref="InsightEndpointsTests"/>. Hosts the BFF API
/// in-process via <see cref="WebApplicationFactory{TEntryPoint}"/>, replaces
/// <see cref="IInsightsAi"/> with a mock so each test asserts against
/// deterministic facade behavior, and provides clients with / without a
/// <c>tid</c> claim so the handler's tid extraction can be exercised.
/// </summary>
public class InsightEndpointsTestFixture : WebApplicationFactory<Program>
{
    public Mock<IInsightsAi> InsightsAiMock { get; } = new(MockBehavior.Loose);

    /// <summary>
    /// Tenant id injected as the <c>tid</c> claim by
    /// <see cref="InsightsAskTenantFakeAuthHandler"/>. Tests assert that the
    /// handler extracts this value and passes it as
    /// <see cref="InsightsAgentRequest.TenantId"/>.
    /// </summary>
    public const string TestTenantId = "00000000-0000-0000-0000-000000000abc";

    /// <summary>Test user oid claim — matches CustomWebAppFactory's identity convention.</summary>
    public const string TestUserOid = "test-user-00000000-0000-0000-0000-insights000061";

    /// <summary>Test bearer token; the fake auth handler accepts any non-empty value.</summary>
    public const string TestBearerToken = "insights-ask-test-token";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Provide the same configuration baseline CustomWebAppFactory uses so all
        // production option validators pass.
        builder.ConfigureHostConfiguration(config =>
        {
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
                // Cosmos persistence — must satisfy AiPersistenceModule's hard-failed
                // validation; Cosmos calls themselves never happen in this suite
                // (no test path reaches Cosmos-backed services).
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
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // Replace the real IInsightsAi binding with the mock so we drive the
            // success / decline / throw paths deterministically.
            services.RemoveAll<IInsightsAi>();
            services.AddSingleton(InsightsAiMock.Object);

            // Remove background hosted services that depend on external infrastructure.
            services.RemoveAll<IHostedService>();

            // Mock IDataverseService so any incidental Dataverse-touching registration
            // in the pipeline doesn't blow up at construction time.
            var dataverseMock = new Mock<IDataverseService>();
            dataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseMock.Object);
        });
    }

    /// <summary>
    /// Authenticated client whose token includes a <c>tid</c> claim — the
    /// standard tenant-user path. The handler reads <c>tid</c> to populate
    /// <see cref="InsightsAgentRequest.TenantId"/>.
    /// </summary>
    public HttpClient CreateAuthenticatedTenantClient() => CreateClientWithTid(includeTid: true, includeAuth: true);

    /// <summary>
    /// Authenticated client whose token has NO <c>tid</c> claim — exercises
    /// the 401 ProblemDetails path for malformed tokens.
    /// </summary>
    public HttpClient CreateAuthenticatedClientWithoutTenantClaim()
        => CreateClientWithTid(includeTid: false, includeAuth: true);

    /// <summary>
    /// Client with NO Authorization header — exercises the
    /// <c>RequireAuthorization()</c> 401 path.
    /// </summary>
    public HttpClient CreateUnauthenticatedTenantClient()
        => CreateClientWithTid(includeTid: true, includeAuth: false);

    /// <summary>
    /// Authenticated tenant client with an additional <c>Insights:Playbooks:Map</c>
    /// configuration overlay, so tests can exercise the canonical-name → Guid
    /// resolution path on /api/insights/ask. Map entries are injected as in-memory
    /// configuration and bound by <see cref="Sprk.Bff.Api.Api.Insights.InsightsPlaybookNameMapOptions"/>.
    /// </summary>
    public HttpClient CreateAuthenticatedTenantClientWithNameMap(Dictionary<string, Guid> map)
    {
        var factory = this.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(config =>
            {
                var dict = new Dictionary<string, string?>();
                foreach (var kvp in map)
                {
                    // IConfiguration binder reads "Insights:Playbooks:Map:<name>" = "<guid>"
                    dict[$"Insights:Playbooks:Map:{kvp.Key}"] = kvp.Value.ToString();
                }
                config.AddInMemoryCollection(dict);
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new InsightsAskAuthOptions(true));

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = InsightsAskTenantFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = InsightsAskTenantFakeAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, InsightsAskTenantFakeAuthHandler>(
                    InsightsAskTenantFakeAuthHandler.SchemeName, _ => { });

                services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = InsightsAskTenantFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = InsightsAskTenantFakeAuthHandler.SchemeName;
                });
            });
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestBearerToken);
        return client;
    }

    private HttpClient CreateClientWithTid(bool includeTid, bool includeAuth)
    {
        var factory = this.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new InsightsAskAuthOptions(includeTid));

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = InsightsAskTenantFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = InsightsAskTenantFakeAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, InsightsAskTenantFakeAuthHandler>(
                    InsightsAskTenantFakeAuthHandler.SchemeName, _ => { });

                services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = InsightsAskTenantFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = InsightsAskTenantFakeAuthHandler.SchemeName;
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

/// <summary>
/// Toggle injected by <see cref="InsightEndpointsTestFixture"/> to control
/// whether the test auth handler emits a <c>tid</c> claim. Required because
/// the D-P15 endpoint additionally requires <c>tid</c> on top of <c>oid</c>.
/// </summary>
internal sealed class InsightsAskAuthOptions
{
    public bool IncludeTid { get; }
    public InsightsAskAuthOptions(bool includeTid) => IncludeTid = includeTid;
}

/// <summary>
/// Fake auth handler that emits <c>oid</c> (+ optional <c>tid</c>) for D-P15
/// endpoint tests. Used exclusively here so tests can exercise both the
/// tid-present (200 OK) and tid-absent (401) paths without disturbing other
/// suites that use <see cref="CustomWebAppFactory"/>.
/// </summary>
internal sealed class InsightsAskTenantFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "InsightsAskTenantFakeAuth";

    private readonly InsightsAskAuthOptions _opts;

    public InsightsAskTenantFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        InsightsAskAuthOptions opts)
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
            new("oid", InsightEndpointsTestFixture.TestUserOid),
            new(ClaimTypes.NameIdentifier, InsightEndpointsTestFixture.TestUserOid),
            new(ClaimTypes.Name, "Insights Ask Test User"),
            new("name", "Insights Ask Test User"),
        };

        if (_opts.IncludeTid)
        {
            claims.Add(new Claim("tid", InsightEndpointsTestFixture.TestTenantId));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
