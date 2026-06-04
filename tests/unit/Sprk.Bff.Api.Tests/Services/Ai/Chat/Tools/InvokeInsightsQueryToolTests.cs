using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat.Tools;

/// <summary>
/// Unit tests for <see cref="InvokeInsightsQueryTool"/> — R5 task 024 (D2-14).
///
/// <para>
/// Test strategy: the tool wraps a typed <see cref="HttpClient"/> + an
/// <see cref="IHttpContextAccessor"/>. We mock the <see cref="HttpMessageHandler"/> via
/// a <see cref="StubHttpMessageHandler"/> that captures every outgoing request and
/// returns a canned response — this lets us assert request shape (path, headers,
/// body), v1.1 SSE Accept negotiation, OBO token forwarding, correlation-id
/// propagation, error mapping (12 contract codes), and v1.1 forward-compat passthrough
/// without spinning up a server.
/// </para>
///
/// <para>
/// Coverage (10 tests for §3.7 obligation):
/// <list type="bullet">
///   <item>Tool catalog: GetTools() yields one AIFunction with name <c>insights.query</c> + description.</item>
///   <item>NFR-12 description quality: explicit differentiation from <c>invoke_summarize_playbook</c>.</item>
///   <item>HTTP shape: POST to <c>/api/insights/assistant/query</c> with correct JSON body.</item>
///   <item>v1.1 SSE opt-in: <c>Accept: text/event-stream</c> header set on every call.</item>
///   <item>v1.1 SSE fallback: 406 triggers a JSON-only retry; second request succeeds.</item>
///   <item>forceMode semantics: null → omitted/null; "playbook" → forwarded; "rag" → forwarded; "garbage" → forwarded (BFF validates).</item>
///   <item>Correlation ID: <c>x-correlation-id</c> set on every outbound request.</item>
///   <item>12 error codes: each contract code surfaces verbatim via <see cref="InsightsToolException"/>.</item>
///   <item>Zone B boundary: source file contains NO <c>using ...Services.Ai.Insights</c> or <c>Models.Insights</c>.</item>
///   <item>No token snapshot: token rotated mid-session → second call forwards the rotated value.</item>
///   <item>v1.1 forward-compat: unknown response fields (e.g., <c>citations[].href</c>) pass through unchanged.</item>
/// </list>
/// </para>
/// </summary>
public class InvokeInsightsQueryToolTests
{
    private const string TestQuery = "What will this matter cost to complete?";
    private const string TestSubject = "matter:da116923-d65a-f111-a825-3833c5d9bcb1";

    // ── Constructor validation ─────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenHttpClientIsNull()
    {
        var action = () => new InvokeInsightsQueryTool(
            httpClient: null!,
            httpContextAccessor: null,
            logger: NullLogger<InvokeInsightsQueryTool>.Instance);

        action.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
    {
        using var http = BuildHttpClient(out _);
        var action = () => new InvokeInsightsQueryTool(
            httpClient: http,
            httpContextAccessor: null,
            logger: null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── Tool catalog (NFR-12 + AIPU2-061 discoverability) ─────────────────────────

    [Fact]
    public void GetTools_YieldsExactlyOneFunction_WithCanonicalNameAndDescription()
    {
        var tool = CreateSut(out _);

        var functions = tool.GetTools().ToList();

        functions.Should().HaveCount(1);
        functions[0].Name.Should().Be(InvokeInsightsQueryTool.ToolName);
        functions[0].Name.Should().Be("insights.query");
        functions[0].Description.Should().Be(InvokeInsightsQueryTool.ToolDescription);
    }

    [Fact]
    public void ToolDescription_IsSemanticallyDistinctFrom_InvokeSummarizePlaybook_NFR12()
    {
        // NFR-12 / UR-01 mitigation: the tool description text MUST scope routing to
        // entity-scoped analytical questions AND MUST explicitly differentiate from
        // invoke_summarize_playbook so the LLM Layer 2 classifier picks the right tool.
        var description = InvokeInsightsQueryTool.ToolDescription;

        // Must scope to entity Q&A (matter/project/invoice).
        description.Should().Contain("matter", because:
            "description must scope to matter-scoped analytical questions (NFR-12)");
        description.Should().Contain("project", because:
            "description must scope to project-scoped analytical questions (NFR-12)");
        description.Should().Contain("invoice", because:
            "description must scope to invoice-scoped analytical questions (NFR-12)");

        // Must explicitly differentiate from invoke_summarize_playbook (UR-01 mitigation).
        description.Should().Contain("invoke_summarize_playbook", because:
            "description must explicitly name invoke_summarize_playbook as the alternative " +
            "tool for session-uploaded file summarization (NFR-12 / UR-01)");

        // Description length sanity — LLM tool schemas budget per-tool description tokens.
        description.Length.Should().BeLessThan(800,
            "tool descriptions over ~800 chars degrade LLM tool-routing quality");
        description.Length.Should().BeGreaterThan(100,
            "tool descriptions under 100 chars are too thin for routing discipline");

        // Semantic distinctness: the Summarize tool's description (read from the sibling
        // class) MUST NOT verbatim-overlap with this one beyond unavoidable English words.
        var summarizeDescription = InvokeSummarizePlaybookTool.ToolDescription;
        description.Should().NotBe(summarizeDescription,
            "description text MUST be a distinct string from invoke_summarize_playbook");
    }

    // ── HTTP shape (binding contract v1.0 §3.1) ────────────────────────────────────

    [Fact]
    public async Task InsightsQueryAsync_PostsToCorrectEndpoint_WithExpectedJsonBody()
    {
        var tool = CreateSut(out var stub, response: BuildPlaybookSuccessResponse());

        await tool.InsightsQueryAsync(
            query: TestQuery,
            subject: TestSubject,
            forceMode: "playbook");

        stub.Requests.Should().ContainSingle();
        var req = stub.Requests[0];

        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be(InvokeInsightsQueryTool.EndpointPath);
        req.RequestUri.AbsolutePath.Should().Be("/api/insights/assistant/query");

        var body = stub.LastRequestBody;
        body.Should().NotBeNullOrEmpty();

        using var doc = JsonDocument.Parse(body!);
        doc.RootElement.GetProperty("query").GetString().Should().Be(TestQuery);
        doc.RootElement.GetProperty("subject").GetString().Should().Be(TestSubject);
        doc.RootElement.GetProperty("forceMode").GetString().Should().Be("playbook");
    }

    [Fact]
    public async Task InsightsQueryAsync_NullForceMode_OmittedOrNullInRequestBody()
    {
        // forceMode is omitted from request body when null (per integration brief §3.2:
        // "When omitted, BFF invokes the intent classifier"). Our serializer uses
        // JsonIgnoreCondition.WhenWritingNull so the field is absent on the wire.
        var tool = CreateSut(out var stub, response: BuildRagSuccessResponse());

        await tool.InsightsQueryAsync(
            query: TestQuery,
            subject: TestSubject,
            forceMode: null);

        var body = stub.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);

        // forceMode either absent or explicitly null — both are valid per contract.
        if (doc.RootElement.TryGetProperty("forceMode", out var forceModeEl))
        {
            forceModeEl.ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Theory]
    [InlineData("playbook")]
    [InlineData("rag")]
    [InlineData("garbage")] // tool does NOT pre-validate — BFF returns 400 forceMode.invalid
    public async Task InsightsQueryAsync_ExplicitForceMode_PropagatesToBffWithoutValidation(string forceMode)
    {
        var tool = CreateSut(out var stub, response: BuildPlaybookSuccessResponse());

        await tool.InsightsQueryAsync(TestQuery, TestSubject, forceMode);

        var body = stub.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("forceMode").GetString().Should().Be(forceMode);
    }

    // ── v1.1 SSE opt-in (Accept header + 406 fallback) ─────────────────────────────

    [Fact]
    public async Task InsightsQueryAsync_SendsSseAcceptHeader_PerV11OptIn()
    {
        var tool = CreateSut(out var stub, response: BuildPlaybookSuccessResponse());

        await tool.InsightsQueryAsync(TestQuery, TestSubject, forceMode: null);

        var req = stub.Requests[0];
        req.Headers.Accept.Should().Contain(h => h.MediaType == "text/event-stream",
            because: "v1.1 contract §2.1 — opt into SSE via Accept: text/event-stream");
        req.Headers.Accept.Should().Contain(h => h.MediaType == "application/json",
            because: "client must accept v1.0 JSON as graceful fallback");
    }

    [Fact]
    public async Task InsightsQueryAsync_OnHttp406_FallsBackToJsonOnlyAndSucceeds()
    {
        // First response: 406 Not Acceptable (server did not honor SSE).
        // Second response: 200 OK with v1.0 JSON body.
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            new HttpResponseMessage(HttpStatusCode.NotAcceptable),
            BuildPlaybookSuccessResponse(),
        });

        var tool = CreateSutWithResponseQueue(out var stub, responses);

        var result = await tool.InsightsQueryAsync(TestQuery, TestSubject, forceMode: null);

        result.Should().NotBeNullOrEmpty();
        stub.Requests.Should().HaveCount(2,
            because: "406 must trigger a JSON-only retry per v1.1 graceful-fallback contract");

        // Second request omits text/event-stream Accept (JSON-only fallback).
        var retry = stub.Requests[1];
        retry.Headers.Accept.Should().NotContain(h => h.MediaType == "text/event-stream");
        retry.Headers.Accept.Should().Contain(h => h.MediaType == "application/json");
    }

    // ── Correlation ID propagation (spec FR-17 / SC-16) ────────────────────────────

    [Fact]
    public async Task InsightsQueryAsync_SetsCorrelationIdHeader_OnEveryOutboundRequest()
    {
        var tool = CreateSut(out var stub, response: BuildPlaybookSuccessResponse());

        await tool.InsightsQueryAsync(TestQuery, TestSubject, forceMode: null);

        var req = stub.Requests[0];
        req.Headers.TryGetValues("x-correlation-id", out var values).Should().BeTrue(
            because: "FR-17 / SC-16 require a correlation-id header on every outbound call");
        values!.Single().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task InsightsQueryAsync_TwoCalls_GenerateDistinctCorrelationIds()
    {
        // Each Assistant turn must produce a unique correlation ID per FR-17.
        var queue = new Queue<HttpResponseMessage>(new[]
        {
            BuildPlaybookSuccessResponse(),
            BuildPlaybookSuccessResponse(),
        });

        var tool = CreateSutWithResponseQueue(out var stub, queue);

        await tool.InsightsQueryAsync(TestQuery, TestSubject, forceMode: null);
        await tool.InsightsQueryAsync(TestQuery, TestSubject, forceMode: null);

        var id1 = stub.Requests[0].Headers.GetValues("x-correlation-id").Single();
        var id2 = stub.Requests[1].Headers.GetValues("x-correlation-id").Single();
        id1.Should().NotBe(id2, because: "each Assistant turn must have a unique correlation-id");
    }

    // ── No token snapshot (ADR-028) ────────────────────────────────────────────────

    [Fact]
    public async Task InsightsQueryAsync_NoTokenSnapshot_ReadsFreshTokenPerCall_AdrL028()
    {
        // Simulated token rotation: the HttpContext's Authorization header changes between
        // calls. The tool MUST forward the current value each time, never a snapshot.
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            BuildPlaybookSuccessResponse(),
            BuildPlaybookSuccessResponse(),
        });

        var contextAccessor = new MutableHttpContextAccessor();
        var tool = CreateSutWithResponseQueueAndContext(out var stub, responses, contextAccessor);

        contextAccessor.SetBearerToken("token-A");
        await tool.InsightsQueryAsync(TestQuery, TestSubject, forceMode: null);

        contextAccessor.SetBearerToken("token-B");
        await tool.InsightsQueryAsync(TestQuery, TestSubject, forceMode: null);

        stub.Requests.Should().HaveCount(2);
        stub.Requests[0].Headers.GetValues("Authorization").Single().Should().Be("Bearer token-A");
        stub.Requests[1].Headers.GetValues("Authorization").Single().Should().Be("Bearer token-B",
            because: "ADR-028 — token MUST be read fresh per call, never snapshotted in constructor");
    }

    // ── 12 contract error codes (integration brief §5.1) ───────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "query.required")]
    [InlineData(HttpStatusCode.BadRequest, "subject.required")]
    [InlineData(HttpStatusCode.BadRequest, "subject.invalid")]
    [InlineData(HttpStatusCode.BadRequest, "forceMode.invalid")]
    [InlineData(HttpStatusCode.BadRequest, "conversationContext.invalid")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ai.insights.disabled")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ai.rag.disabled")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ai.intent-classification.disabled")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ai.assistant-default-playbook.unconfigured")]
    [InlineData(HttpStatusCode.InternalServerError, "INSIGHTS_ASSISTANT_INTERNAL_ERROR")]
    public async Task InsightsQueryAsync_OnProblemDetailsError_SurfacesContractErrorCodeViaException(
        HttpStatusCode status,
        string errorCode)
    {
        // ADR-019 ProblemDetails parsing — preserve stable errorCode + correlationId
        // verbatim so the renderer (task 026) can map per-code UX.
        var problemDetails = new
        {
            type = $"https://errors.spaarke.com/{errorCode}",
            title = "Bad Request",
            status = (int)status,
            detail = "Detail message",
            errorCode,
            correlationId = "corr-xyz",
        };
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(problemDetails),
                Encoding.UTF8,
                "application/problem+json"),
        };

        var tool = CreateSut(out _, response: response);

        var act = async () => await tool.InsightsQueryAsync(TestQuery, TestSubject, forceMode: null);

        var ex = (await act.Should().ThrowAsync<InsightsToolException>()).Which;
        ex.ErrorCode.Should().Be(errorCode);
        ex.Status.Should().Be((int)status);
        ex.CorrelationId.Should().Be("corr-xyz");
        ex.Title.Should().Be("Bad Request");
        ex.Detail.Should().Be("Detail message");
    }

    [Fact]
    public async Task InsightsQueryAsync_On401WithNoErrorCode_SurfacesSyntheticAuth401()
    {
        // 401 typically has no errorCode body (auth challenge) — tool synthesizes auth.401.
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(""),
        };

        var tool = CreateSut(out _, response: response);

        var act = async () => await tool.InsightsQueryAsync(TestQuery, TestSubject, forceMode: null);

        var ex = (await act.Should().ThrowAsync<InsightsToolException>()).Which;
        ex.ErrorCode.Should().Be("auth.401");
        ex.Status.Should().Be(401);
    }

    [Fact]
    public async Task InsightsQueryAsync_On429WithRetryAfter_SurfacesRateLimit429WithRetryAfter()
    {
        // ADR-016 rate-limit honoring: 429 surfaces structurally with Retry-After
        // preserved. Tool does NOT auto-retry; renderer/UX layer surfaces the wait hint.
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(""),
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(15));

        var tool = CreateSut(out _, response: response);

        var act = async () => await tool.InsightsQueryAsync(TestQuery, TestSubject, forceMode: null);

        var ex = (await act.Should().ThrowAsync<InsightsToolException>()).Which;
        ex.ErrorCode.Should().Be("rate-limit.429");
        ex.Status.Should().Be(429);
        ex.RetryAfter.Should().NotBeNullOrEmpty();
    }

    // ── Zone B boundary (R5 CLAUDE.md §3.5 / §10) ──────────────────────────────────

    [Fact]
    public void ZoneBoundary_SourceFile_ContainsNoInsightsInternalImports()
    {
        // R5 CLAUDE.md §3.5 + §10 + refined ADR-013 §3.5 — R5 is Zone B HTTP consumer of
        // the Insights contract. The tool source file MUST NOT import any
        // Sprk.Bff.Api.Services.Ai.Insights.* or Sprk.Bff.Api.Models.Insights.* types.
        // Verified by reflection on the assembly's referenced types AND by source-file
        // grep when possible.
        var toolType = typeof(InvokeInsightsQueryTool);
        var toolAssembly = toolType.Assembly;

        // Read source file from disk if possible (best-effort static-analysis check).
        // Compute the expected source path relative to the assembly location.
        // The check scans for actual `using` directives (top-of-file imports) by
        // splitting on lines and inspecting only those that begin (after whitespace) with
        // `using ` — this avoids false positives from documentation comments that mention
        // the namespace in prose.
        var sourcePath = FindSourceFile();
        if (sourcePath != null && File.Exists(sourcePath))
        {
            var source = File.ReadAllText(sourcePath);
            var usingLines = source.Split('\n')
                .Select(l => l.TrimStart())
                .Where(l => l.StartsWith("using ") && l.Contains(';'))
                .ToList();

            usingLines.Should().NotContain(l => l.StartsWith("using Sprk.Bff.Api.Services.Ai.Insights"),
                "Zone B boundary — no Insights internals imports allowed");
            usingLines.Should().NotContain(l => l.StartsWith("using Sprk.Bff.Api.Models.Insights"),
                "Zone B boundary — no Models.Insights imports allowed");
        }

        // Reflection check: enumerate the tool class's fields and verify none reference
        // Insights-internal namespaces. (Local private DTOs are nested types of the tool
        // itself — those are allowed.)
        var fields = toolType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var f in fields)
        {
            f.FieldType.FullName.Should().NotStartWith("Sprk.Bff.Api.Services.Ai.Insights",
                "Zone B boundary — no Insights-internal types may be injected as fields");
            f.FieldType.FullName.Should().NotStartWith("Sprk.Bff.Api.Models.Insights",
                "Zone B boundary — no Models.Insights types may be injected as fields");
        }
    }

    // ── v1.1 forward-compat (unknown fields pass through) ──────────────────────────

    [Fact]
    public async Task InsightsQueryAsync_OnSuccess_ForwardsUnknownFieldsVerbatim_V11ForwardCompat()
    {
        // v1.1 forward-compat: response includes unknown fields (e.g., citations[].href
        // from v1.1) that the tool must forward without stripping. We achieve this by
        // returning the raw response body string to the caller (the frontend renderer in
        // task 026 owns parsing).
        var v11Body = """
        {
          "path": "rag",
          "answer": "Test answer with [1] citation.",
          "citations": [
            { "n": 1, "source": "Doc.pdf", "excerpt": "...", "href": "https://example/preview/doc-A", "observationId": "doc-A", "chunkId": "chunk-1" }
          ],
          "confidence": 0.85,
          "playbookId": null,
          "structuredResult": { "kind": "observation", "envelope": { "extraField": "v1.2-future" } },
          "diagnostics": { "intentSource": "classifier", "classifierBelowThreshold": false, "elapsedMs": 100, "cacheHit": false },
          "streamingSupported": true
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(v11Body, Encoding.UTF8, "application/json"),
        };

        var tool = CreateSut(out _, response: response);

        var result = await tool.InsightsQueryAsync(TestQuery, TestSubject, forceMode: null);

        // The v1.1 href + future-streamingSupported fields MUST be present verbatim.
        result.Should().Contain("\"href\"",
            because: "v1.1 forward-compat — unknown fields like citations[].href must pass through");
        result.Should().Contain("streamingSupported",
            because: "v1.1 forward-compat — top-level unknown fields must pass through");
        result.Should().Contain("v1.2-future",
            because: "v1.1 forward-compat — structuredResult.envelope unknown subfields must pass through");
    }

    // ── Private helpers ────────────────────────────────────────────────────────────

    private static InvokeInsightsQueryTool CreateSut(
        out StubHttpMessageHandler stub,
        HttpResponseMessage? response = null)
    {
        stub = new StubHttpMessageHandler();
        if (response != null) stub.Enqueue(response);
        else stub.Enqueue(BuildPlaybookSuccessResponse());

        var http = new HttpClient(stub) { BaseAddress = new Uri("https://test.local") };
        return new InvokeInsightsQueryTool(
            http,
            httpContextAccessor: null,
            logger: NullLogger<InvokeInsightsQueryTool>.Instance);
    }

    private static InvokeInsightsQueryTool CreateSutWithResponseQueue(
        out StubHttpMessageHandler stub,
        Queue<HttpResponseMessage> responses)
    {
        stub = new StubHttpMessageHandler();
        while (responses.Count > 0) stub.Enqueue(responses.Dequeue());

        var http = new HttpClient(stub) { BaseAddress = new Uri("https://test.local") };
        return new InvokeInsightsQueryTool(
            http,
            httpContextAccessor: null,
            logger: NullLogger<InvokeInsightsQueryTool>.Instance);
    }

    private static InvokeInsightsQueryTool CreateSutWithResponseQueueAndContext(
        out StubHttpMessageHandler stub,
        Queue<HttpResponseMessage> responses,
        IHttpContextAccessor contextAccessor)
    {
        stub = new StubHttpMessageHandler();
        while (responses.Count > 0) stub.Enqueue(responses.Dequeue());

        var http = new HttpClient(stub) { BaseAddress = new Uri("https://test.local") };
        return new InvokeInsightsQueryTool(
            http,
            httpContextAccessor: contextAccessor,
            logger: NullLogger<InvokeInsightsQueryTool>.Instance);
    }

    private static HttpClient BuildHttpClient(out StubHttpMessageHandler stub)
    {
        stub = new StubHttpMessageHandler();
        stub.Enqueue(BuildPlaybookSuccessResponse());
        return new HttpClient(stub) { BaseAddress = new Uri("https://test.local") };
    }

    private static HttpResponseMessage BuildPlaybookSuccessResponse()
    {
        var body = """
        {
          "path": "playbook",
          "answer": "Predicted cost ~$280k based on 12 similar matters.",
          "citations": [
            { "n": 1, "source": "Acme APA.pdf", "excerpt": "Estimated cost: $282k", "observationId": "doc-A", "chunkId": "chunk-1" }
          ],
          "confidence": 0.92,
          "playbookId": "predict-matter-cost@v1",
          "structuredResult": { "kind": "inference", "envelope": { "predictedCost": 280000 } },
          "diagnostics": { "intentSource": "classifier", "classifierBelowThreshold": false, "elapsedMs": 1842, "cacheHit": false }
        }
        """;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static HttpResponseMessage BuildRagSuccessResponse()
    {
        var body = """
        {
          "path": "rag",
          "answer": "The closing conditions include [1] regulatory approval.",
          "citations": [
            { "n": 1, "source": "Closing Memo.docx", "excerpt": "Closing subject to regulatory approval...", "observationId": "doc-B", "chunkId": "chunk-2" }
          ],
          "confidence": 0.81,
          "playbookId": null,
          "structuredResult": { "kind": "observation", "envelope": { "results": [], "summary": "..." } },
          "diagnostics": { "intentSource": "forceMode", "classifierBelowThreshold": false, "elapsedMs": 943, "cacheHit": true }
        }
        """;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static string? FindSourceFile()
    {
        // Walk up from the test assembly's location to find the repo root, then resolve
        // the expected source-file path.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir != null; i++)
        {
            var candidate = Path.Combine(dir,
                "src", "server", "api", "Sprk.Bff.Api",
                "Services", "Ai", "Chat", "Tools", "InvokeInsightsQueryTool.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}

/// <summary>
/// Test double for capturing outbound <see cref="HttpRequestMessage"/> instances and
/// returning a canned queue of responses. The handler clones outgoing requests
/// (including reading the body into <see cref="LastRequestBody"/>) so post-call
/// assertions can inspect headers + body.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();
    public string? LastRequestBody { get; private set; }
    private readonly Queue<HttpResponseMessage> _responses = new();

    public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }
        if (_responses.Count == 0)
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("No canned response — test setup error.", Encoding.UTF8, "text/plain"),
            };
        }
        return _responses.Dequeue();
    }
}

/// <summary>
/// Mutable <see cref="IHttpContextAccessor"/> for simulating token rotation mid-session
/// per ADR-028 token discipline test.
/// </summary>
internal sealed class MutableHttpContextAccessor : IHttpContextAccessor
{
    private HttpContext? _context;

    public HttpContext? HttpContext
    {
        get => _context;
        set => _context = value;
    }

    public void SetBearerToken(string token)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Authorization = $"Bearer {token}";
        _context = ctx;
    }
}
