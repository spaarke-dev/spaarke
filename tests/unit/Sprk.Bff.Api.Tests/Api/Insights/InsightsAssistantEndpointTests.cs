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
/// Integration tests for the Wave E3 task 042 public endpoint
/// <c>POST /api/insights/assistant/query</c> (FR-05 unified Spaarke Assistant tool-call).
/// </summary>
/// <remarks>
/// <para>
/// <b>Coverage</b>: tests are organized per the task 042 POML sub-task B.5 acceptance:
/// <list type="number">
///   <item>Playbook intent → routes to artifact path; <see cref="InsightsAssistantQueryResponse.StructuredResult"/>.Kind = "inference"</item>
///   <item>RAG intent → routes to RAG path; citations present</item>
///   <item>forceMode=playbook → bypasses classifier (classifier never called)</item>
///   <item>forceMode=rag → bypasses classifier (classifier never called)</item>
///   <item>Low-confidence classifier → falls back to RAG (classifier-fallback intentSource)</item>
///   <item>Classifier kill-switch (no forceMode) → 503 ai.intent-classification.disabled</item>
///   <item>RAG kill-switch (forceMode=rag) → 503 ai.rag.disabled</item>
///   <item>Unauthenticated → 401</item>
///   <item>Missing tid claim → 401 ProblemDetails</item>
///   <item>Missing query → 400 query.required</item>
///   <item>Invalid subject scheme → 400 subject.invalid</item>
///   <item>Invalid forceMode → 400 forceMode.invalid</item>
/// </list>
/// </para>
/// <para>
/// <b>What's mocked</b>: <see cref="IInsightsAi"/> is fully mocked (deterministic behavior).
/// The classifier + RAG + playbook layers are never invoked directly — they live behind
/// the facade in production. This matches the Wave E1 + E2 test pattern.
/// </para>
/// </remarks>
public class InsightsAssistantEndpointTests : IClassFixture<InsightsAssistantEndpointTestFixture>
{
    private readonly InsightsAssistantEndpointTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private const string SampleMatterSubject = "matter:11111111-2222-3333-4444-555555555555";

    public InsightsAssistantEndpointTests(InsightsAssistantEndpointTestFixture fixture)
    {
        _fixture = fixture;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PLAYBOOK PATH — classifier-routed
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_PlaybookIntent_RoutesToPlaybookPath_AndIncludesStructuredInference()
    {
        // Arrange — facade returns a playbook-path response with kind=inference.
        var playbookEnvelope = """{"type":"inference","id":"inf:42","predicate":"predictedCost"}""";
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssistantQueryFacadeResult
            {
                Path = "playbook",
                Answer = "Predicted cost is $280k based on 12 comparable matters.",
                Citations = [],
                Confidence = 0.74,
                PlaybookId = "predict-matter-cost@v1",
                StructuredKind = "inference",
                StructuredEnvelopeJson = playbookEnvelope,
                IntentSource = "classifier",
                ClassifierBelowThreshold = false,
                CacheHit = false,
                DurationMs = 1842,
                HitCount = 0
            });

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "What is the predicted cost?", subject = SampleMatterSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        // Assert — basic shape
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InsightsAssistantQueryResponse>(_jsonOptions);
        body!.Path.Should().Be("playbook");
        body.PlaybookId.Should().Be("predict-matter-cost@v1");
        body.StructuredResult.Kind.Should().Be("inference");
        body.Answer.Should().Contain("$280k");
        body.Diagnostics.IntentSource.Should().Be("classifier");
        body.Diagnostics.ClassifierBelowThreshold.Should().BeFalse();

        // Headers per contract §6.2
        response.Headers.GetValues("X-Insights-Path").Should().ContainSingle().Which.Should().Be("playbook");
        response.Headers.GetValues("X-Insights-Intent-Source").Should().ContainSingle().Which.Should().Be("classifier");
        response.Headers.GetValues("X-Insights-Elapsed-Ms").Should().ContainSingle().Which.Should().Be("1842");

        // Envelope round-trip — Kind says inference; Envelope.type must echo it.
        body.StructuredResult.Envelope.GetProperty("type").GetString().Should().Be("inference");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RAG PATH — classifier-routed
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_RagIntent_RoutesToRagPath_AndIncludesCitations()
    {
        // Arrange — facade returns a RAG-path response with citations [1] [2].
        var ragEnvelope = """{"results":[],"summary":"summary"}""";
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssistantQueryFacadeResult
            {
                Path = "rag",
                Answer = "Three risk themes: indemnity [1], governing law [2], earn-out timing.",
                Citations = new[]
                {
                    new AssistantQueryCitation(1, "Acme APA.pdf", "Indemnity capped at $5M", "doc-A", "chunk-A-7"),
                    new AssistantQueryCitation(2, "Beta APA.pdf", "Governing law: NY", "doc-B", "chunk-B-3")
                },
                Confidence = 0.87,
                PlaybookId = null,
                StructuredKind = "observation",
                StructuredEnvelopeJson = ragEnvelope,
                IntentSource = "classifier",
                ClassifierBelowThreshold = false,
                CacheHit = false,
                DurationMs = 612,
                HitCount = 2
            });

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "Summarize the risks", subject = SampleMatterSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InsightsAssistantQueryResponse>(_jsonOptions);
        body!.Path.Should().Be("rag");
        body.PlaybookId.Should().BeNull();
        body.StructuredResult.Kind.Should().Be("observation");
        body.Citations.Should().HaveCount(2);
        body.Citations[0].N.Should().Be(1);
        body.Citations[0].Source.Should().Be("Acme APA.pdf");
        body.Citations[1].N.Should().Be(2);
        body.Answer.Should().Contain("[1]").And.Contain("[2]");

        response.Headers.GetValues("X-Insights-Path").Should().ContainSingle().Which.Should().Be("rag");
        response.Headers.GetValues("X-Insights-Hit-Count").Should().ContainSingle().Which.Should().Be("2");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FORCE-MODE OVERRIDES — Assistant tells BFF the intent
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_ForceModePlaybook_ForwardsForceModeToFacade()
    {
        AssistantQueryFacadeRequest? captured = null;
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AssistantQueryFacadeRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new AssistantQueryFacadeResult
            {
                Path = "playbook",
                Answer = "ok",
                StructuredKind = "inference",
                IntentSource = "forceMode",
                StructuredEnvelopeJson = "{}"
            });

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "predict cost", subject = SampleMatterSubject, forceMode = "playbook" };

        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.ForceMode.Should().Be("playbook",
            "endpoint MUST forward forceMode unchanged so the handler bypasses classifier");

        var body = await response.Content.ReadFromJsonAsync<InsightsAssistantQueryResponse>(_jsonOptions);
        body!.Diagnostics.IntentSource.Should().Be("forceMode");
        response.Headers.GetValues("X-Insights-Intent-Source").Should().ContainSingle().Which.Should().Be("forceMode");
    }

    [Fact]
    public async Task Post_ForceModeRag_ForwardsForceModeToFacade()
    {
        AssistantQueryFacadeRequest? captured = null;
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AssistantQueryFacadeRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new AssistantQueryFacadeResult
            {
                Path = "rag",
                Answer = "ok",
                StructuredKind = "observation",
                IntentSource = "forceMode",
                StructuredEnvelopeJson = "{}"
            });

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "summarize", subject = SampleMatterSubject, forceMode = "rag" };

        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.ForceMode.Should().Be("rag");
    }

    [Fact]
    public async Task Post_LowConfidenceClassifier_FacadeReportsClassifierFallback()
    {
        // Arrange — facade ran the classifier, got below-threshold, fell back to RAG.
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssistantQueryFacadeResult
            {
                Path = "rag",
                Answer = "fallback summary",
                StructuredKind = "observation",
                IntentSource = "classifier-fallback",
                ClassifierBelowThreshold = true,
                StructuredEnvelopeJson = "{}"
            });

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "ambiguous question", subject = SampleMatterSubject };

        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InsightsAssistantQueryResponse>(_jsonOptions);
        body!.Diagnostics.IntentSource.Should().Be("classifier-fallback");
        body.Diagnostics.ClassifierBelowThreshold.Should().BeTrue();
        response.Headers.GetValues("X-Insights-Intent-Source").Should().ContainSingle().Which.Should().Be("classifier-fallback");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // KILL-SWITCH MATRIX — contract §7
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_ClassifierDisabled_Returns503WithErrorCode()
    {
        // Arrange — Null classifier surfaces FeatureDisabledException through the facade.
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureDisabledException(
                "ai.intent-classification.disabled",
                "Insights intent classification requires Insights feature enabled."));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = SampleMatterSubject };

        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("ai.intent-classification.disabled");
    }

    [Fact]
    public async Task Post_RagDisabled_WithForceModeRag_Returns503WithErrorCode()
    {
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureDisabledException(
                "ai.rag.disabled",
                "RAG services require Analysis:Enabled + DocumentIntelligence:Enabled + AI Search keys."));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = SampleMatterSubject, forceMode = "rag" };

        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("ai.rag.disabled");
    }

    [Fact]
    public async Task Post_DefaultPlaybookUnconfigured_Returns503WithSpecificErrorCode()
    {
        // Arrange — handler couldn't resolve default playbook Guid for forceMode=playbook.
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Default playbook 'predict-matter-cost@v1' is not configured in 'Insights:Playbooks:Map'."));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = SampleMatterSubject, forceMode = "playbook" };

        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "default-playbook unconfigured is a deployment error surfaced as 503 per contract §5.1");
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("ai.assistant-default-playbook.unconfigured");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUTH — 401 per ADR-008 + ADR-019
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Unauthenticated_Returns401()
    {
        var client = _fixture.CreateUnauthenticatedTenantClient();
        var request = new { query = "q", subject = SampleMatterSubject };

        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_MissingTenantClaim_Returns401WithProblemDetails()
    {
        var client = _fixture.CreateAuthenticatedClientWithoutTenantClaim();
        var request = new { query = "q", subject = SampleMatterSubject };

        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // VALIDATION — 400 ProblemDetails with stable errorCode per contract §5.1
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_MissingQuery_Returns400_QueryRequired()
    {
        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = (string?)null, subject = SampleMatterSubject };

        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("query.required",
            "contract §5.1 stable errorCode for missing query");
    }

    [Fact]
    public async Task Post_InvalidSubject_Returns400_SubjectInvalid()
    {
        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = "document:abc-123" };

        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("subject.invalid",
            "contract §5.1 stable errorCode for unknown scheme");
    }

    [Fact]
    public async Task Post_InvalidForceMode_Returns400_ForceModeInvalid()
    {
        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = SampleMatterSubject, forceMode = "xyz" };

        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("forceMode.invalid",
            "contract §5.1 stable errorCode for invalid forceMode value");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INTERNAL ERROR — facade exception surfaces as 500 ProblemDetails
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_FacadeThrows_Returns500WithStableErrorCode()
    {
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationException("simulated upstream failure"));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "q", subject = SampleMatterSubject };

        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("simulated upstream failure",
            "ADR-019: never leak internal exception details to the wire");
        raw.Should().Contain("INSIGHTS_ASSISTANT_INTERNAL_ERROR",
            "stable error code per ADR-019 helper convention");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REGISTRATION
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_EndpointRegistered()
    {
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssistantQueryFacadeResult
            {
                Path = "rag",
                Answer = "x",
                StructuredKind = "observation",
                IntentSource = "classifier",
                StructuredEnvelopeJson = "{}"
            });

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { query = "x", subject = SampleMatterSubject };

        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "MapInsightsAssistantEndpoint() registered in EndpointMappingExtensions.cs");
    }
}

/// <summary>
/// Test fixture for <see cref="InsightsAssistantEndpointTests"/>. Hosts the BFF API
/// in-process via <see cref="WebApplicationFactory{TEntryPoint}"/>, replaces
/// <see cref="IInsightsAi"/> with a mock, and provides clients with / without a
/// <c>tid</c> claim. Mirrors the <c>InsightsSearchEndpointTestFixture</c> pattern.
/// </summary>
public class InsightsAssistantEndpointTestFixture : WebApplicationFactory<Program>
{
    public Mock<IInsightsAi> InsightsAiMock { get; } = new(MockBehavior.Loose);

    public const string TestTenantId = "00000000-0000-0000-0000-000000000def";
    public const string TestUserOid = "test-user-00000000-0000-0000-0000-insights000042";
    public const string TestBearerToken = "insights-assistant-test-token";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            // Mirror InsightsSearchEndpointTestFixture baseline so production option
            // validators pass. Keep in sync if production option validation changes.
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
                services.AddSingleton(new InsightsAssistantAuthOptions(includeTid));

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = InsightsAssistantTenantFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = InsightsAssistantTenantFakeAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, InsightsAssistantTenantFakeAuthHandler>(
                    InsightsAssistantTenantFakeAuthHandler.SchemeName, _ => { });

                services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = InsightsAssistantTenantFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = InsightsAssistantTenantFakeAuthHandler.SchemeName;
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

internal sealed class InsightsAssistantAuthOptions
{
    public bool IncludeTid { get; }
    public InsightsAssistantAuthOptions(bool includeTid) => IncludeTid = includeTid;
}

internal sealed class InsightsAssistantTenantFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "InsightsAssistantTenantFakeAuth";
    private readonly InsightsAssistantAuthOptions _opts;

    public InsightsAssistantTenantFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        InsightsAssistantAuthOptions opts)
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
            new("oid", InsightsAssistantEndpointTestFixture.TestUserOid),
            new(ClaimTypes.NameIdentifier, InsightsAssistantEndpointTestFixture.TestUserOid),
            new(ClaimTypes.Name, "Insights Assistant Test User"),
            new("name", "Insights Assistant Test User"),
        };

        if (_opts.IncludeTid)
        {
            claims.Add(new Claim("tid", InsightsAssistantEndpointTestFixture.TestTenantId));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
