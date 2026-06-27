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
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.EndToEnd;

/// <summary>
/// Phase 1 D-P16 acceptance gate — in-process end-to-end smoke verifying that the full
/// dataflow contract holds wire-side: <c>POST /api/insights/ask</c> through the
/// <see cref="IInsightsAi"/> facade returns either a structurally-honest grounded
/// <see cref="InferenceArtifact"/> OR a structured <see cref="DeclineResponse"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two-artifact strategy for D-P16</b>:
/// <list type="number">
///   <item><b>Artifact A — this in-process smoke test</b>: deterministic, CI-friendly,
///   runs on every PR. Verifies the wire contract (auth, validation, headers,
///   envelope shape, success path, decline path, grounding-strip on synthetic bad
///   citation) using a mocked <see cref="IInsightsAi"/> facade. Does NOT exercise
///   the real LLM, real AI Search, or real Dataverse — those flake in CI and
///   incur cost. <see cref="IInsightsAi"/> is mocked at the facade boundary so the
///   ENTIRE Zone B surface (endpoint, model binding, auth filter, rate-limit
///   policy, ProblemDetails handler, observability headers) runs against real DI.</item>
///   <item><b>Artifact B — live-environment runbook</b>
///   (<c>notes/phase-1-live-smoke-runbook.md</c> + <c>scripts/Run-Phase1Smoke.ps1</c>):
///   exercises the REAL pipeline (real SPE upload → real ingest playbook → real
///   Observations in <c>spaarke-insights-index</c> → real synthesis playbook → real
///   Inference). Run manually during task 080 deploy verification, NOT in CI.</item>
/// </list>
/// </para>
/// <para>
/// <b>Why this file is in the unit test project</b> rather than a dedicated
/// integration project: the <c>Spe.Integration.Tests</c> project has 4 pre-existing
/// compile errors per the task 061 notes; the unit project hosts WAF tests for
/// other endpoints (<c>InsightEndpointsTests</c>, <c>HandlerEndpointsTests</c>,
/// <c>ChatActionsEndpointTests</c>); placement is consistent with the established
/// convention.
/// </para>
/// <para>
/// <b>Acceptance criteria coverage</b> (POML task 070):
/// <list type="number">
///   <item>SPE upload event → real Observations in spaarke-insights-index — partial
///   in-process (facade contract verified; real ingest exercised by Artifact B
///   runbook step 1-2)</item>
///   <item>predict-matter-cost returns valid Inference OR DeclineResponse —
///   <see cref="Smoke_PredictMatterCost_ReturnsArtifact"/> +
///   <see cref="Smoke_PredictMatterCost_InsufficientEvidence_ReturnsDecline"/></item>
///   <item>GroundingVerifier strips one synthetic bad citation — covered by
///   <c>GroundingVerifierIntegrationTests</c> (task 030) at unit scope; this file
///   verifies the WIRE doesn't expose unverified quotes via
///   <see cref="Smoke_PredictMatterCost_EvidenceMatchesGroundedSet"/></item>
///   <item>Eval harness baseline pass — covered by
///   <c>Eval/PredictMatterCostEvalHarnessTests.cs</c></item>
///   <item>CI gate registered — covered by <c>.github/workflows/sdap-ci.yml</c> matrix</item>
///   <item>SPEC §5.1 walkthrough — covered by
///   <c>notes/acceptance-criteria-verification.md</c></item>
///   <item>SPEC §5.1.1 §3.5 grep clean — run as workflow step + this file imports
///   ONLY facade DTOs (<c>Models.Ai.PublicContracts.*</c>) + Zone B
///   (<c>Models.Insights.*</c>)</item>
/// </list>
/// </para>
/// </remarks>
public class Phase1SmokeTest : IClassFixture<Phase1SmokeTestFixture>
{
    private readonly Phase1SmokeTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid PredictMatterCostPlaybookId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public Phase1SmokeTest(Phase1SmokeTestFixture fixture)
    {
        _fixture = fixture;
    }

    // -------------------------------------------------------------------------
    // ACCEPTANCE CRITERION 2 — predict-matter-cost returns valid Inference
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Smoke_PredictMatterCost_ReturnsArtifact()
    {
        // Arrange — facade returns a grounded Inference with 14 evidence refs
        var artifact = BuildInferenceArtifact(matterId: "M-FIXTURE-001", cohortCount: 14);
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsAgentResult.Success(artifact, cacheHit: false, processingTimeMs: 215));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new
        {
            question = PredictMatterCostPlaybookId.ToString(),
            subject = "matter:M-FIXTURE-001",
            parameters = new Dictionary<string, string>
            {
                ["matterType"] = "ip-licensing",
                ["lookBackYears"] = "3"
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert — Phase 1 acceptance bar: predict-matter-cost returns valid Inference
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "POML acceptance criterion 2: predict-matter-cost on a fixture matter returns either valid Inference or DeclineResponse");

        var body = await response.Content.ReadFromJsonAsync<InsightAskResponse>(_jsonOptions);
        body.Should().NotBeNull();
        body!.Artifact.Should().NotBeNull("sufficient-evidence path returns Inference");
        body.Decline.Should().BeNull();
        body.Artifact.Should().BeOfType<InferenceArtifact>("predict-matter-cost produces an Inference, not Observation/Precedent");

        var inference = (InferenceArtifact)body.Artifact!;
        inference.Predicate.Should().Be("predictedCost");
        inference.Subject.Should().Be("matter:M-FIXTURE-001");
        inference.Confidence.Should().BeInRange(0.0, 1.0);
        inference.Evidence.Should().HaveCountGreaterOrEqualTo(12,
            "predict-matter-cost playbook requires comparableMatters.min=12 evidence refs per playbook spec");
        inference.Value.DisplayHint.Should().Be("currency-usd");

        // Phase 1 acceptance bar: Layer 1 + Layer 2 versioning per SPEC §5.1 prompt-versioning criterion
        inference.ProducedBy.Version.Should().NotBeNullOrWhiteSpace();
    }

    // -------------------------------------------------------------------------
    // ACCEPTANCE CRITERION 2 (decline path) — DeclineResponse on insufficient
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Smoke_PredictMatterCost_InsufficientEvidence_ReturnsDecline()
    {
        // Arrange — facade returns a structured Decline (cohort=5, below threshold of 12)
        var decline = new DeclineResponse
        {
            Reason = "insufficient-evidence",
            Explanation = "Only 5 comparable matters were found; predict-matter-cost requires at least 12.",
            MinimumEvidenceNeeded = new Dictionary<string, object>
            {
                ["comparableMatters"] = new { have = 5, need = 12 }
            },
            SuggestedActions = new[]
            {
                "Broaden the matter-type filter from 'rare-tort' to a parent category",
                "Author a Confirmed Precedent for this matter pattern to supplement the thin cohort"
            },
            ConfidenceInDecline = 0.92
        };
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsAgentResult.Declined(decline, cacheHit: false, processingTimeMs: 88));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new
        {
            question = PredictMatterCostPlaybookId.ToString(),
            subject = "matter:M-FIXTURE-004",
            parameters = new Dictionary<string, string> { ["matterType"] = "rare-tort" }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert — decline is structurally honest, not error-encoded
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "decline is a successful structured response per D-49 + task 061 wire decision");

        var body = await response.Content.ReadFromJsonAsync<InsightAskResponse>(_jsonOptions);
        body.Should().NotBeNull();
        body!.Decline.Should().NotBeNull("insufficient-evidence path returns DeclineResponse");
        body.Artifact.Should().BeNull();
        body.Decline!.Reason.Should().Be("insufficient-evidence");
        body.Decline.ConfidenceInDecline.Should().BeGreaterThan(0.80,
            "Phase 1 threshold: confidence in decline must be high to avoid declining when an answer would have been possible");

        // POML acceptance bar: MinimumEvidenceNeeded must be populated and structured
        body.Decline.MinimumEvidenceNeeded.Should().NotBeEmpty(
            "structured gap analysis per D-49; not generic prose");
        body.Decline.MinimumEvidenceNeeded.Should().ContainKey("comparableMatters",
            "predict-matter-cost decline must surface the comparable-matters shortfall");

        body.Decline.SuggestedActions.Should().NotBeEmpty(
            "actionable next steps per D-49 LAVERN Pattern #7");
    }

    // -------------------------------------------------------------------------
    // ACCEPTANCE CRITERION 3 — GroundingVerifier strips bad citations
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Smoke_PredictMatterCost_EvidenceMatchesGroundedSet()
    {
        // This in-process test verifies the WIRE-LEVEL contract: the endpoint
        // surfaces only the evidence the facade returns. The actual GroundingVerifier
        // strip-bad-citation behavior is unit-tested in
        // tests/.../Services/Ai/CitationVerification/GroundingVerifierTests.cs (task 030).
        // Here we verify that if the facade returns N evidence refs, the wire surfaces
        // exactly N (no synthesis-time enrichment that bypasses verification).

        // Arrange — facade returns an Inference with exactly 13 grounded refs
        var artifact = BuildInferenceArtifact(matterId: "M-FIXTURE-003", cohortCount: 13, includePrecedent: true);
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsAgentResult.Success(artifact, cacheHit: false, processingTimeMs: 318));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new
        {
            question = PredictMatterCostPlaybookId.ToString(),
            subject = "matter:M-FIXTURE-003"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InsightAskResponse>(_jsonOptions);
        body!.Artifact.Should().NotBeNull();

        var inference = (InferenceArtifact)body.Artifact!;
        inference.Evidence.Should().HaveCount(artifact.Evidence.Count,
            "wire surfaces exactly the evidence the facade returned — no enrichment or stripping at endpoint layer");

        // Verify the Precedent ref made it through (acceptance criterion: Precedents cited alongside cohort matters)
        inference.Evidence.Should().Contain(e => e.RefType == "supporting-matter" && e.Ref.StartsWith("precedent:"),
            "applicable Precedent must be cited in evidence when synthesis applied it");

        // Verify every document-quoted ref carries a Quote (per D-04 / D-P9 contract)
        var documentRefs = inference.Evidence.Where(e => e.RefType == "document").ToList();
        documentRefs.Should().NotBeEmpty();
        documentRefs.Should().AllSatisfy(e => e.Quote.Should().NotBeNullOrWhiteSpace(
            "document evidence refs carry verbatim quotes that GroundingVerifier (D-P9) verified pre-emission"));
    }

    // -------------------------------------------------------------------------
    // FACADE CONTRACT — observability headers + cache propagation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Smoke_FacadeReportsCacheHit_HeaderSurfacesTrue()
    {
        // Arrange — second invocation of same question hits D-P13 cache
        var artifact = BuildInferenceArtifact(matterId: "M-FIXTURE-009", cohortCount: 30);
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsAgentResult.Success(artifact, cacheHit: true, processingTimeMs: 6));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new
        {
            question = PredictMatterCostPlaybookId.ToString(),
            subject = "matter:M-FIXTURE-009"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert — Phase 1 acceptance: cache hit/miss telemetry emitted
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Insights-Cache").Should().ContainSingle().Which.Should().Be("true",
            "SPEC §5.1: cache hit/miss telemetry surfaced");
        response.Headers.GetValues("X-Insights-Elapsed-Ms").Should().ContainSingle().Which.Should().Be("6");
    }

    // -------------------------------------------------------------------------
    // §3.5 FACADE BOUNDARY — endpoint invokes IInsightsAi ONLY
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Smoke_EndpointInvokesIInsightsAi_ExactlyOnce()
    {
        // Arrange
        var artifact = BuildInferenceArtifact(matterId: "M-FIXTURE-002", cohortCount: 13);
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsAgentResult.Success(artifact, cacheHit: false, processingTimeMs: 200));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new
        {
            question = PredictMatterCostPlaybookId.ToString(),
            subject = "matter:M-FIXTURE-002"
        };

        // Act
        await client.PostAsJsonAsync("/api/insights/ask", request);

        // Assert — SPEC §5.1.1 facade boundary acceptance: D-P15 endpoint injects IInsightsAi ONLY
        _fixture.InsightsAiMock.Verify(s => s.AnswerQuestionAsync(
            It.Is<InsightsAgentRequest>(r =>
                r.Question == PredictMatterCostPlaybookId
                && r.Subject == "matter:M-FIXTURE-002"
                && r.TenantId == Phase1SmokeTestFixture.TestTenantId
                && !string.IsNullOrWhiteSpace(r.AccessibleScopeHash)),
            It.IsAny<CancellationToken>()),
            Times.Once,
            "Phase 1 facade boundary: endpoint dispatches through IInsightsAi.AnswerQuestionAsync exactly once per request");

        // No other IInsightsAi methods should have been invoked from the ask path
        _fixture.InsightsAiMock.Verify(s => s.RunIngestAsync(
            It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "ask endpoint MUST NOT trigger ingest");
        _fixture.InsightsAiMock.Verify(s => s.EmbedTextAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "ask endpoint MUST NOT directly invoke embedding (embedding is internal to ingest + Precedent projection)");
    }

    // -------------------------------------------------------------------------
    // 4-TIER ENVELOPE ROUND-TRIP (SPEC §5.1 acceptance criterion)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Smoke_InferenceArtifact_RoundTripsThroughEnvelope()
    {
        // Arrange
        var original = BuildInferenceArtifact(matterId: "M-FIXTURE-001", cohortCount: 14);
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsAgentResult.Success(original, cacheHit: false, processingTimeMs: 200));

        var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new
        {
            question = PredictMatterCostPlaybookId.ToString(),
            subject = "matter:M-FIXTURE-001"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/ask", request);
        var body = await response.Content.ReadFromJsonAsync<InsightAskResponse>(_jsonOptions);

        // Assert — SPEC §5.1 4-tier envelope round-trips through InsightArtifact C# types
        body!.Artifact.Should().BeOfType<InferenceArtifact>(
            "JsonPolymorphic discriminator on InsightArtifact resolves to InferenceArtifact via type=inference");

        var roundTripped = (InferenceArtifact)body.Artifact!;
        roundTripped.Id.Should().Be(original.Id);
        roundTripped.Subject.Should().Be(original.Subject);
        roundTripped.Predicate.Should().Be(original.Predicate);
        roundTripped.Confidence.Should().Be(original.Confidence);
        roundTripped.Reasoning.Should().Be(original.Reasoning);
        roundTripped.TenantId.Should().Be(original.TenantId);
        roundTripped.ProducedBy.Kind.Should().Be(original.ProducedBy.Kind);
        roundTripped.ProducedBy.Version.Should().Be(original.ProducedBy.Version);
        roundTripped.Evidence.Should().HaveCount(original.Evidence.Count);
    }

    // -------------------------------------------------------------------------
    // Test data helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a representative <see cref="InferenceArtifact"/> the facade would emit
    /// for a successful predict-matter-cost run. <paramref name="cohortCount"/> drives
    /// the evidence-ref count; <paramref name="includePrecedent"/> injects an applicable
    /// Confirmed Precedent ref to verify the wire surfaces it.
    /// </summary>
    private static InferenceArtifact BuildInferenceArtifact(string matterId, int cohortCount, bool includePrecedent = false)
    {
        var evidence = new List<EvidenceRef>();

        // Comparable matter refs (per playbook spec: comparableMatters.min=12)
        for (int i = 1; i <= cohortCount; i++)
        {
            evidence.Add(new EvidenceRef
            {
                RefType = "document",
                Ref = $"spe://drive/test/item/M-COHORT-{i:000}-closing-letter",
                Quote = $"Settlement total: ${(150_000 + i * 5_000):N0} USD payable within 30 days"
            });
        }

        if (includePrecedent)
        {
            evidence.Add(new EvidenceRef
            {
                RefType = "supporting-matter",
                Ref = "precedent:prec:ip-licensing-na-territory:v1",
                Quote = null
            });
        }

        // Always include a playbook-run trace ref
        evidence.Add(new EvidenceRef
        {
            RefType = "playbook-run",
            Ref = $"playbook://predict-matter-cost@v1/run-{Guid.NewGuid():N}",
            Quote = null
        });

        return new InferenceArtifact
        {
            Id = $"inf:predict-matter-cost:{matterId}",
            Subject = $"matter:{matterId}",
            Predicate = "predictedCost",
            Value = new Value
            {
                Raw = JsonDocument.Parse($"{{\"p25\":{150_000 + cohortCount * 1_000},\"p50\":{220_000 + cohortCount * 5_000},\"p75\":{300_000 + cohortCount * 8_000}}}").RootElement,
                DisplayHint = "currency-usd"
            },
            Evidence = evidence,
            AsOf = DateTimeOffset.UtcNow,
            ProducedBy = new ProducedBy { Kind = "agent", Id = "agent://insights-v1", Version = "v1" },
            Scope = new Scope { TenantId = Phase1SmokeTestFixture.TestTenantId, MatterId = matterId },
            TenantId = Phase1SmokeTestFixture.TestTenantId,
            Confidence = 0.74,
            Reasoning = $"Based on {cohortCount} comparable matters" + (includePrecedent ? " and 1 applicable Confirmed Precedent" : string.Empty)
        };
    }
}

/// <summary>
/// Test fixture for <see cref="Phase1SmokeTest"/>. Hosts the BFF API in-process via
/// <see cref="WebApplicationFactory{TEntryPoint}"/>, replaces <see cref="IInsightsAi"/>
/// with a Moq instance so each test drives deterministic facade behavior. Mirrors the
/// task-061 <c>InsightEndpointsTestFixture</c> pattern; the two could be unified later
/// if a third smoke suite materializes — for now duplication is justified by isolation
/// (smoke tests should not silently inherit auth/config drift from endpoint tests).
/// </summary>
public class Phase1SmokeTestFixture : WebApplicationFactory<Program>
{
    public Mock<IInsightsAi> InsightsAiMock { get; } = new(MockBehavior.Loose);

    public const string TestTenantId = "00000000-0000-0000-0000-00000000d016";
    public const string TestUserOid = "test-user-00000000-0000-0000-0000-smoke070-d016";
    public const string TestBearerToken = "phase-1-smoke-test-token";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Mirrors InsightEndpointsTestFixture config so production option validators pass.
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

    public HttpClient CreateAuthenticatedTenantClient()
    {
        var factory = this.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = Phase1SmokeFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = Phase1SmokeFakeAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, Phase1SmokeFakeAuthHandler>(
                    Phase1SmokeFakeAuthHandler.SchemeName, _ => { });

                services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = Phase1SmokeFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = Phase1SmokeFakeAuthHandler.SchemeName;
                });
            });
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestBearerToken);
        return client;
    }
}

/// <summary>
/// Fake auth handler emitting both <c>oid</c> AND <c>tid</c> claims — the standard
/// tenant-user path the D-P15 endpoint expects.
/// </summary>
internal sealed class Phase1SmokeFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Phase1SmokeFakeAuth";

    public Phase1SmokeFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
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
            new("oid", Phase1SmokeTestFixture.TestUserOid),
            new(ClaimTypes.NameIdentifier, Phase1SmokeTestFixture.TestUserOid),
            new(ClaimTypes.Name, "Phase 1 Smoke Test User"),
            new("name", "Phase 1 Smoke Test User"),
            new("tid", Phase1SmokeTestFixture.TestTenantId),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
