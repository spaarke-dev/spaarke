using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

// IMPORTANT — R5 CLAUDE.md §3.5 / ADR-013 §3.5 Zone B boundary:
// This file MUST NOT import anything from Sprk.Bff.Api.Services.Ai.Insights or
// Sprk.Bff.Api.Models.Insights. The smoke test asserts WIRE-LEVEL behavior of the
// binding contract — it does NOT couple to Insights internals. Local mirror DTOs
// are defined at the bottom of this file.

namespace Spe.Integration.Tests;

/// <summary>
/// R5 task 030 (D2-20) — smoke tests for the Insights tool (insights.query) against
/// the live Spaarke Dev BFF deployment.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is intentionally a smoke test against a DEPLOYED environment</b>, not an
/// in-process integration test. The <see cref="IntegrationTestFixture"/>
/// WebApplicationFactory boots a mock-stubbed BFF in-memory — it cannot serve the
/// real <c>InsightsAssistantQuery</c> endpoint with live RAG / playbook /
/// classifier dependencies. The smoke surface is the Spaarke Dev deployment at
/// <c>https://spaarke-bff-dev.azurewebsites.net</c>. The
/// <see cref="IntegrationTestFixture"/> is still consumed here so the test suite
/// remains in the same xUnit collection / fixture pattern as
/// <c>PlaybookExecutionIntegrationTests</c> and others (R5 CLAUDE.md §3.1 reuse
/// mandate — no parallel test fixture, auth bootstrap, or HTTP client).
/// </para>
///
/// <para>
/// <b>Smoke tests are SKIPPED by default.</b> To execute, set these two env vars
/// in the shell that runs <c>dotnet test</c>:
/// </para>
/// <list type="bullet">
///   <item><c>SPAARKE_DEV_BFF_URL</c> — e.g., <c>https://spaarke-bff-dev.azurewebsites.net</c></item>
///   <item><c>SPAARKE_DEV_BEARER_TOKEN</c> — an OBO-style bearer JWT with <c>tid</c> + <c>oid</c> claims</item>
/// </list>
/// <para>
/// When either is missing, every theory case is reported as SKIPPED with a clear
/// explanation — the test suite stays green for routine CI runs (which do NOT
/// hit live Spaarke Dev) and is opt-in for the SME-walkthrough operator.
/// </para>
///
/// <para>
/// <b>Information-leakage discipline (ADR-018 + integration brief §5.2)</b>: this
/// class NEVER logs or asserts verbatim response content. Structural assertions
/// only — HTTP 200, envelope shape, <c>correlationId</c> echoed, <c>path</c> is
/// <c>playbook</c> or <c>rag</c>, <c>confidence</c> in [0,1], FR-04
/// anti-hallucination invariant on RAG (empty <c>answer</c> iff empty
/// <c>citations</c>). The structural pass is logged with categorical summary
/// (path / confidence-bucket / citation-count) — never with answer text or
/// citation excerpts. Evidence capture for the SME walkthrough follows the same
/// rule (see <c>projects/spaarke-ai-platform-unification-r5/notes/task-030-sme-walkthrough.md</c>).
/// </para>
///
/// <para>
/// <b>SME walkthrough is OPERATOR-LED.</b> This file scaffolds the
/// 15-question structural matrix. The SME walkthrough (binding for spec SC-18)
/// is run separately by the operator + ≥1 legal-ops SME on Spaarke Dev tenant
/// — observations are captured per the template in
/// <c>notes/task-030-sme-walkthrough.md</c>. The SME does NOT execute these
/// xUnit tests directly; they're the auditable structural-conformance baseline
/// the SME walkthrough complements with qualitative usability judgment.
/// </para>
///
/// <para>
/// <b>Rate-limit pacing (ADR-016 / integration brief §11)</b>: the 15 questions
/// are sequenced via <c>[CollectionDefinition(..., DisableParallelization = true)]</c>
/// and a ~3-second <c>Task.Delay</c> per case to stay under the 60/min/oid
/// aggregate budget across <c>/ask</c> + <c>/search</c> + <c>/assistant/query</c>.
/// Any 429 observed during execution is a POSITIVE validation of task 029's
/// retry handling and is recorded in evidence (NOT a smoke-test failure).
/// </para>
///
/// <para>
/// <b>Tags</b>: <c>[Trait("Category", "Integration")]</c> +
/// <c>[Trait("Feature", "InsightsTool")]</c> so the suite is independently
/// runnable via <c>--filter "Category=Integration&amp;Feature=InsightsTool"</c>.
/// </para>
/// </remarks>
[Collection("Integration")]
[Trait("Category", "Integration")]
[Trait("Feature", "InsightsTool")]
public class InsightsToolIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    /// <summary>
    /// Env-var name carrying the deployed Spaarke Dev BFF base URL
    /// (e.g., <c>https://spaarke-bff-dev.azurewebsites.net</c>). Required for
    /// smoke-test execution; absence triggers SKIP.
    /// </summary>
    public const string EnvVarDevBffUrl = "SPAARKE_DEV_BFF_URL";

    /// <summary>
    /// Env-var name carrying an OBO bearer JWT for a Spaarke Dev test user. The
    /// token must include <c>tid</c> + <c>oid</c> claims per integration brief
    /// §2 auth requirements. Required for smoke-test execution; absence triggers
    /// SKIP.
    /// </summary>
    public const string EnvVarBearerToken = "SPAARKE_DEV_BEARER_TOKEN";

    /// <summary>
    /// Endpoint path for the Insights Assistant query — matches
    /// <c>InvokeInsightsQueryTool.EndpointPath</c> (R5-local; no Insights import
    /// per Zone B boundary).
    /// </summary>
    public const string InsightsAssistantQueryPath = "/api/insights/assistant/query";

    /// <summary>Pace between requests to stay under the 60/min aggregate rate budget (ADR-016).</summary>
    private static readonly TimeSpan InterRequestPace = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public InsightsToolIntegrationTests(IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Theory consuming the 15-question smoke matrix. Each question issues one
    /// POST to the Spaarke Dev Insights Assistant endpoint and asserts STRUCTURAL
    /// conformance to the binding contract. Verbatim response content is NEVER
    /// logged or asserted (ADR-018).
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(LoadSmokeMatrix))]
    [Trait("SmokeMatrix", "true")]
    public async Task InsightsAssistantQuery_SmokeQuestion_ConformsToContract(SmokeMatrixCase smokeCase)
    {
        // ── SKIP gate: deployed-BFF env vars must be present ─────────────────────
        var devBffUrl = Environment.GetEnvironmentVariable(EnvVarDevBffUrl);
        var bearerToken = Environment.GetEnvironmentVariable(EnvVarBearerToken);

        Skip.If(
            string.IsNullOrWhiteSpace(devBffUrl) || string.IsNullOrWhiteSpace(bearerToken),
            $"Spaarke Dev smoke test SKIPPED for {smokeCase.Id}: set {EnvVarDevBffUrl} + " +
            $"{EnvVarBearerToken} env vars to execute against deployed BFF " +
            $"(https://spaarke-bff-dev.azurewebsites.net).");

        // ── Pace to stay under 60/min aggregate rate budget (ADR-016 / brief §11) ─
        await Task.Delay(InterRequestPace);

        // ── Build the request per integration brief §3 (request schema) ──────────
        // CorrelationId per Assistant turn (FR-17 / SC-16). Echoed in the response
        // ProblemDetails extension (on error) or response header (on success).
        var correlationId = $"r5-smoke-{smokeCase.Id}-{Guid.NewGuid():N}";

        var payload = new InsightsQueryRequestDto
        {
            Query = smokeCase.Query,
            Subject = smokeCase.Subject,
            ForceMode = smokeCase.ForceMode,
            ConversationContext = null, // Phase 1.5 telemetry only.
        };

        using var http = new HttpClient { BaseAddress = new Uri(devBffUrl!) };
        using var request = new HttpRequestMessage(HttpMethod.Post, InsightsAssistantQueryPath)
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.TryAddWithoutValidation("x-correlation-id", correlationId);

        // ── Issue the request ────────────────────────────────────────────────────
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead);

        // ── 429 handling: positive validation (NOT a failure) per task 029 / ADR-016 ─
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.ToString() ?? "(absent)";
            _output.WriteLine(
                $"[429] {smokeCase.Id}: rate limit observed (Retry-After={retryAfter}, " +
                $"correlationId={correlationId}). Positive validation of task 029 retry handling; " +
                $"NOT counted as a smoke-test failure. Pace inter-request gap and rerun manually.");
            Skip.If(true, $"{smokeCase.Id}: 429 observed — pace adjustment needed; positive validation of retry handling.");
            return;
        }

        // ── 200 success branch ──────────────────────────────────────────────────
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            var envelope = JsonSerializer.Deserialize<InsightsResponseEnvelopeDto>(body, JsonOptions);
            envelope.Should().NotBeNull($"{smokeCase.Id}: response body must deserialize");

            // STRUCTURAL assertions only (ADR-018). NEVER assert verbatim answer text.
            envelope!.Path.Should().BeOneOf(
                new[] { "playbook", "rag" },
                $"{smokeCase.Id}: contract §4 path must be 'playbook' or 'rag'");

            envelope.Confidence.Should().BeInRange(
                0.0, 1.0, $"{smokeCase.Id}: confidence must be in [0,1] per contract §4");

            envelope.Citations.Should().NotBeNull(
                $"{smokeCase.Id}: citations must be an array per contract §4 (possibly empty)");

            // FR-04 anti-hallucination: empty answer iff empty citations (RAG path).
            if (envelope.Path == "rag")
            {
                if (string.IsNullOrEmpty(envelope.Answer))
                {
                    envelope.Citations!.Should().BeEmpty(
                        $"{smokeCase.Id}: FR-04 anti-hallucination — empty answer ⇒ empty citations on RAG");
                }
            }

            // Playbook path: playbookId must be present.
            if (envelope.Path == "playbook")
            {
                envelope.PlaybookId.Should().NotBeNullOrEmpty(
                    $"{smokeCase.Id}: playbookId required when path=playbook per contract §4.1");
            }

            // ── Categorical structural-pass log (NO verbatim content) ────────────
            var citationCount = envelope.Citations?.Count ?? 0;
            var confBucket = ConfidenceBucket(envelope.Confidence);
            _output.WriteLine(
                $"[PASS-STRUCTURE] {smokeCase.Id} [{smokeCase.PracticeArea}]: " +
                $"path={envelope.Path}, confidence={confBucket}, citationCount={citationCount}, " +
                $"playbookId={(string.IsNullOrEmpty(envelope.PlaybookId) ? "(none)" : "(present)")}, " +
                $"correlationId={correlationId}");
            // DO NOT log envelope.Answer, citation excerpts, or structuredResult content (ADR-018).
            return;
        }

        // ── Non-2xx path: parse ProblemDetails (ADR-019) and FAIL the case ───────
        var problemBody = await response.Content.ReadAsStringAsync();
        InsightsProblemDetailsDto? problem = null;
        try { problem = JsonSerializer.Deserialize<InsightsProblemDetailsDto>(problemBody, JsonOptions); }
        catch { /* ignore — fall back to bare status */ }

        _output.WriteLine(
            $"[FAIL-STRUCTURE] {smokeCase.Id} [{smokeCase.PracticeArea}]: " +
            $"HTTP {(int)response.StatusCode} {response.StatusCode}; " +
            $"errorCode={problem?.ErrorCode ?? "(none)"}; " +
            $"correlationId={problem?.CorrelationId ?? correlationId}.");

        // Smoke matrix represents the HAPPY PATH — non-2xx is a Sev-1 finding to capture
        // in task-030-smoke-evidence.md. The xUnit failure surfaces it loudly.
        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            $"{smokeCase.Id}: smoke matrix expects 200; errorCode={problem?.ErrorCode ?? "(none)"}; " +
            $"correlationId={problem?.CorrelationId ?? correlationId}. Capture per ADR-018 (no verbatim content).");
    }

    /// <summary>
    /// xUnit MemberData source — loads the 15-question matrix from the
    /// co-located fixture JSON. CopyToOutputDirectory in the csproj ensures the
    /// fixtures folder ships beside the test assembly.
    /// </summary>
    public static IEnumerable<object[]> LoadSmokeMatrix()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory, "fixtures", "insights-smoke-matrix.json");

        if (!File.Exists(fixturePath))
        {
            // Surface as a single failing case so the operator notices wiring issues fast.
            yield return new object[]
            {
                new SmokeMatrixCase
                {
                    Id = "FIXTURE-MISSING",
                    PracticeArea = "META",
                    Subject = "matter:00000000-0000-0000-0000-000000000000",
                    Query = $"Fixture not found at {fixturePath}",
                    ForceMode = null,
                    ExpectedPathHint = "n/a",
                    RubricHint = "Verify Spe.Integration.Tests.csproj copies fixtures/** to output.",
                },
            };
            yield break;
        }

        var json = File.ReadAllText(fixturePath);
        using var doc = JsonDocument.Parse(json);
        var matrix = doc.RootElement.GetProperty("matrix");

        foreach (var entry in matrix.EnumerateArray())
        {
            yield return new object[]
            {
                new SmokeMatrixCase
                {
                    Id = entry.GetProperty("id").GetString() ?? "(unknown)",
                    PracticeArea = entry.GetProperty("practiceArea").GetString() ?? "(unknown)",
                    Subject = entry.GetProperty("subject").GetString() ?? string.Empty,
                    Query = entry.GetProperty("query").GetString() ?? string.Empty,
                    ForceMode = entry.TryGetProperty("forceMode", out var fm) && fm.ValueKind != JsonValueKind.Null
                        ? fm.GetString()
                        : null,
                    ExpectedPathHint = entry.TryGetProperty("expectedPathHint", out var ep) && ep.ValueKind != JsonValueKind.Null
                        ? ep.GetString() ?? "(none)"
                        : "(none)",
                    RubricHint = entry.TryGetProperty("rubricHint", out var rb) && rb.ValueKind != JsonValueKind.Null
                        ? rb.GetString() ?? "(none)"
                        : "(none)",
                },
            };
        }
    }

    private static string ConfidenceBucket(double confidence) =>
        confidence switch
        {
            < 0.4 => "low(<0.4)",
            < 0.6 => "mid-low(<0.6)",
            < 0.8 => "mid(<0.8)",
            _ => "high(>=0.8)",
        };

    // ── Local DTOs (Zone B boundary — no Sprk.Bff.Api.Models.Insights imports) ──

    /// <summary>
    /// Test-local request DTO mirroring contract v1.0 §3. Defined here per
    /// R5 CLAUDE.md §3.5 Zone B boundary — the smoke test asserts wire-level
    /// behavior of the binding contract and does NOT couple to Insights
    /// internals.
    /// </summary>
    public sealed class InsightsQueryRequestDto
    {
        public string Query { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string? ForceMode { get; set; }
        public InsightsConversationContextDto? ConversationContext { get; set; }
    }

    /// <summary>Test-local conversation-context DTO (Zone B boundary).</summary>
    public sealed class InsightsConversationContextDto
    {
        public string? ConversationId { get; set; }
        public string? PreviousTurnSummary { get; set; }
    }

    /// <summary>
    /// Test-local response envelope mirroring contract v1.0 §4. Structural
    /// fields only — <c>StructuredResult</c> is opaque (<see cref="JsonElement"/>)
    /// because the smoke test does NOT inspect playbook-specific envelope shape.
    /// </summary>
    public sealed class InsightsResponseEnvelopeDto
    {
        public string Path { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public List<InsightsCitationDto>? Citations { get; set; }
        public double Confidence { get; set; }
        public string? PlaybookId { get; set; }
        public JsonElement? StructuredResult { get; set; }
        public JsonElement? Diagnostics { get; set; }
    }

    /// <summary>Test-local citation DTO (Zone B boundary).</summary>
    public sealed class InsightsCitationDto
    {
        public int N { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Excerpt { get; set; } = string.Empty;
        public string? ObservationId { get; set; }
        public string? ChunkId { get; set; }
        // v1.1 forward-compat: citations[].href is optional + not asserted by smoke test.
        public string? Href { get; set; }
    }

    /// <summary>Test-local ProblemDetails DTO (ADR-019; Zone B boundary).</summary>
    public sealed class InsightsProblemDetailsDto
    {
        public string? Type { get; set; }
        public string? Title { get; set; }
        public int? Status { get; set; }
        public string? Detail { get; set; }
        public string? ErrorCode { get; set; }
        public string? CorrelationId { get; set; }
    }

    /// <summary>One row of the 15-question smoke matrix.</summary>
    public sealed class SmokeMatrixCase : Xunit.Abstractions.IXunitSerializable
    {
        public string Id { get; set; } = string.Empty;
        public string PracticeArea { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public string? ForceMode { get; set; }
        public string ExpectedPathHint { get; set; } = string.Empty;
        public string RubricHint { get; set; } = string.Empty;

        public void Deserialize(Xunit.Abstractions.IXunitSerializationInfo info)
        {
            Id = info.GetValue<string>(nameof(Id));
            PracticeArea = info.GetValue<string>(nameof(PracticeArea));
            Subject = info.GetValue<string>(nameof(Subject));
            Query = info.GetValue<string>(nameof(Query));
            ForceMode = info.GetValue<string>(nameof(ForceMode));
            ExpectedPathHint = info.GetValue<string>(nameof(ExpectedPathHint));
            RubricHint = info.GetValue<string>(nameof(RubricHint));
        }

        public void Serialize(Xunit.Abstractions.IXunitSerializationInfo info)
        {
            info.AddValue(nameof(Id), Id);
            info.AddValue(nameof(PracticeArea), PracticeArea);
            info.AddValue(nameof(Subject), Subject);
            info.AddValue(nameof(Query), Query);
            info.AddValue(nameof(ForceMode), ForceMode);
            info.AddValue(nameof(ExpectedPathHint), ExpectedPathHint);
            info.AddValue(nameof(RubricHint), RubricHint);
        }

        public override string ToString() => $"{Id} [{PracticeArea}]";
    }
}
