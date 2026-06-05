using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Insights;

/// <summary>
/// Integration tests for the Wave F task 051 (FR-05 v1.1) Server-Sent Events streaming
/// branch of <c>POST /api/insights/assistant/query</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Coverage</b> (7 tests per task 051 POML step 9 acceptance):
/// <list type="number">
///   <item>Accept header negotiation — <c>text/event-stream</c> → SSE; absent → JSON 200</item>
///   <item>RAG path delta sequence — multiple <c>delta</c> events followed by <c>result</c></item>
///   <item>Result event JSON matches v1.0 shape — terminal <c>result</c> chunk carries facade result</item>
///   <item>Playbook path progress sequence — <c>progress</c> events for major phases + <c>result</c></item>
///   <item>Cache-hit short-circuit — <c>progress {step: "cache_hit"}</c> + <c>result</c> + <c>[DONE]</c></item>
///   <item>Mid-stream error → <c>error</c> chunk + <c>[DONE]</c> (no exception propagates)</item>
///   <item>Kill-switch 503 BEFORE stream opens — no SSE body, ProblemDetails response</item>
/// </list>
/// </para>
/// <para>
/// <b>What's mocked</b>: <see cref="IInsightsAi.AssistantQueryStreamAsync"/> returns a
/// hand-crafted <see cref="IAsyncEnumerable{AssistantQueryChunk}"/> — the orchestrator's
/// real streaming flow is exercised in a separate orchestrator unit test (out of scope for
/// this endpoint suite). The endpoint's contract surface (negotiation + frame serialization
/// + DONE sentinel + error projection) is what these tests guarantee.
/// </para>
/// </remarks>
public class InsightsAssistantEndpointStreamingTests : IClassFixture<InsightsAssistantEndpointTestFixture>
{
    private readonly InsightsAssistantEndpointTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private const string SampleMatterSubject = "matter:11111111-2222-3333-4444-555555555555";

    public InsightsAssistantEndpointStreamingTests(InsightsAssistantEndpointTestFixture fixture)
    {
        _fixture = fixture;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T1 — Accept-header negotiation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_NoAcceptHeader_UsesSingleShotJson_BackCompat()
    {
        // Arrange — facade returns a deterministic non-streaming result.
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssistantQueryFacadeResult
            {
                Path = "rag",
                Answer = "single-shot",
                StructuredKind = "observation",
                IntentSource = "classifier",
                StructuredEnvelopeJson = "{}"
            });

        var client = _fixture.CreateAuthenticatedTenantClient();
        // Explicitly remove Accept header (HttpClient may default to */*; that's also OK).
        client.DefaultRequestHeaders.Accept.Clear();
        var request = new { query = "q", subject = SampleMatterSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        // Assert — single-shot JSON path, NOT SSE.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json",
            "v1.0 clients without Accept: text/event-stream MUST receive single-shot JSON per R5 §2.6");
    }

    [Fact]
    public async Task Post_AcceptEventStream_UsesSseStream_AndEmitsResultThenDone()
    {
        // Arrange — facade emits 1 result chunk via the stream API.
        var terminalResult = new AssistantQueryFacadeResult
        {
            Path = "rag",
            Answer = "streamed answer",
            StructuredKind = "observation",
            IntentSource = "classifier",
            StructuredEnvelopeJson = "{}"
        };
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryStreamAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(
                new AssistantQueryChunk { Type = "result", Result = terminalResult }));

        var client = _fixture.CreateAuthenticatedTenantClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var request = new { query = "q", subject = SampleMatterSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        // Assert — SSE response with correct content-type + [DONE] sentinel.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("event: result");
        body.Should().Contain("data: [DONE]");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T2 — RAG path delta sequence
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_AcceptEventStream_RagPath_EmitsDeltaSequence()
    {
        // Arrange — facade emits 3 delta chunks then a result chunk.
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryStreamAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(
                new AssistantQueryChunk { Type = "progress", Step = "rag_search_started" },
                new AssistantQueryChunk { Type = "delta", Path = "answer", Content = "Hello", Sequence = 1 },
                new AssistantQueryChunk { Type = "delta", Path = "answer", Content = " ", Sequence = 2 },
                new AssistantQueryChunk { Type = "delta", Path = "answer", Content = "world", Sequence = 3 },
                new AssistantQueryChunk
                {
                    Type = "result",
                    Result = new AssistantQueryFacadeResult
                    {
                        Path = "rag",
                        Answer = "Hello world",
                        StructuredKind = "observation",
                        IntentSource = "classifier",
                        StructuredEnvelopeJson = "{}"
                    }
                }));

        var client = _fixture.CreateAuthenticatedTenantClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var request = new { query = "q", subject = SampleMatterSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert — three delta events with sequence + content, in order.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var deltaEvents = ExtractEventsByType(body, "delta");
        deltaEvents.Should().HaveCount(3, "facade emitted 3 delta chunks");

        // Sequence ordering preserved.
        deltaEvents[0].Should().Contain("\"sequence\":1").And.Contain("\"content\":\"Hello\"");
        deltaEvents[1].Should().Contain("\"sequence\":2");
        deltaEvents[2].Should().Contain("\"sequence\":3").And.Contain("\"content\":\"world\"");

        body.Should().Contain("data: [DONE]");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T3 — Result event JSON matches v1.0 shape
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_AcceptEventStream_ResultEvent_CarriesV1FacadeShape()
    {
        // Arrange — facade emits a fully-populated result.
        var terminalResult = new AssistantQueryFacadeResult
        {
            Path = "rag",
            Answer = "final answer with [1] citation",
            Citations = new[]
            {
                new AssistantQueryCitation(1, "Acme.pdf", "snippet", "obs-1", "chunk-1")
            },
            Confidence = 0.91,
            StructuredKind = "observation",
            StructuredEnvelopeJson = """{"results":[],"summary":"x"}""",
            IntentSource = "classifier",
            DurationMs = 812,
            HitCount = 1
        };

        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryStreamAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(
                new AssistantQueryChunk { Type = "result", Result = terminalResult }));

        var client = _fixture.CreateAuthenticatedTenantClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var request = new { query = "q", subject = SampleMatterSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert — extract the result event, deserialize the inner Result object, and verify it's
        // a structurally complete AssistantQueryFacadeResult.
        var resultEvents = ExtractEventsByType(body, "result");
        resultEvents.Should().HaveCount(1);

        using var doc = JsonDocument.Parse(resultEvents[0]);
        var inner = doc.RootElement.GetProperty("result");
        inner.GetProperty("path").GetString().Should().Be("rag");
        inner.GetProperty("answer").GetString().Should().Be("final answer with [1] citation");
        inner.GetProperty("confidence").GetDouble().Should().Be(0.91);
        inner.GetProperty("intentSource").GetString().Should().Be("classifier");
        inner.GetProperty("durationMs").GetInt64().Should().Be(812);
        inner.GetProperty("hitCount").GetInt32().Should().Be(1);
        inner.GetProperty("citations")[0].GetProperty("source").GetString().Should().Be("Acme.pdf");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T4 — Playbook path progress sequence
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_AcceptEventStream_PlaybookPath_EmitsProgressSequence()
    {
        // Arrange — facade emits progress chunks for playbook phases + result.
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryStreamAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(
                new AssistantQueryChunk { Type = "progress", Step = "classifier_started" },
                new AssistantQueryChunk { Type = "progress", Step = "classifier_complete", Content = "playbook" },
                new AssistantQueryChunk { Type = "progress", Step = "playbook_started", Content = "predict-matter-cost@v1" },
                new AssistantQueryChunk { Type = "progress", Step = "playbook_complete" },
                new AssistantQueryChunk
                {
                    Type = "result",
                    Result = new AssistantQueryFacadeResult
                    {
                        Path = "playbook",
                        Answer = "answer",
                        PlaybookId = "predict-matter-cost@v1",
                        StructuredKind = "inference",
                        IntentSource = "classifier",
                        StructuredEnvelopeJson = """{"type":"inference"}"""
                    }
                }));

        var client = _fixture.CreateAuthenticatedTenantClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var request = new { query = "predict cost", subject = SampleMatterSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert — at least 4 progress events with the expected step labels.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var progressEvents = ExtractEventsByType(body, "progress");
        progressEvents.Should().HaveCountGreaterThanOrEqualTo(4);
        body.Should().Contain("\"step\":\"classifier_started\"");
        body.Should().Contain("\"step\":\"classifier_complete\"");
        body.Should().Contain("\"step\":\"playbook_started\"");
        body.Should().Contain("\"step\":\"playbook_complete\"");

        // Result event still emitted at the end.
        body.Should().Contain("event: result");
        body.Should().Contain("data: [DONE]");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T5 — Cache-hit short-circuit
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_AcceptEventStream_CacheHit_EmitsSingleProgressThenResult()
    {
        // Arrange — facade emits cache_hit progress + result only.
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryStreamAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(
                new AssistantQueryChunk { Type = "progress", Step = "cache_hit" },
                new AssistantQueryChunk
                {
                    Type = "result",
                    Result = new AssistantQueryFacadeResult
                    {
                        Path = "playbook",
                        Answer = "cached",
                        PlaybookId = "predict-matter-cost@v1",
                        StructuredKind = "inference",
                        IntentSource = "classifier",
                        CacheHit = true,
                        StructuredEnvelopeJson = "{}"
                    }
                }));

        var client = _fixture.CreateAuthenticatedTenantClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var request = new { query = "q", subject = SampleMatterSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert — cache_hit progress + result + DONE, no delta sequence.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"step\":\"cache_hit\"");
        body.Should().Contain("event: result");
        body.Should().Contain("\"cacheHit\":true");
        body.Should().NotContain("event: delta",
            "cache-hit short-circuit MUST NOT emit delta events per F1 spike Section D");
        body.Should().Contain("data: [DONE]");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T6 — Mid-stream error → error chunk + DONE (no exception propagates)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_AcceptEventStream_MidStreamError_EmitsErrorEventThenDone()
    {
        // Arrange — facade emits one progress chunk then throws mid-stream. The endpoint
        // MUST convert the throw to an `error` SSE frame followed by `[DONE]` per
        // mini-plan §6 decision 4 (no exception leaks to the wire).
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryStreamAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable(
                new AssistantQueryChunk { Type = "progress", Step = "classifier_started" },
                new InvalidOperationException("simulated mid-stream failure")));

        var client = _fixture.CreateAuthenticatedTenantClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var request = new { query = "q", subject = SampleMatterSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert — error frame with stable error code + DONE sentinel; internal message NOT
        // surfaced (ADR-019).
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "mid-stream errors MUST surface via error frame, not by tearing down the response");
        body.Should().Contain("event: error");
        body.Should().Contain("INSIGHTS_ASSISTANT_STREAM_ERROR",
            "stable error code per ADR-019 helper convention");
        body.Should().NotContain("simulated mid-stream failure",
            "ADR-019: never leak internal exception details to the wire");
        body.Should().Contain("data: [DONE]");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T7 — Kill-switch BEFORE stream opens → 503 ProblemDetails, no SSE body
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_AcceptEventStream_KillSwitchPreStream_Returns503WithoutSseBody()
    {
        // Arrange — facade's stream throws FeatureDisabledException on the FIRST chunk
        // (the orchestrator's pre-stream classifier call). The endpoint MUST return 503
        // ProblemDetails with NO SSE body (no Content-Type: text/event-stream).
        _fixture.InsightsAiMock.Reset();
        _fixture.InsightsAiMock
            .Setup(s => s.AssistantQueryStreamAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ImmediatelyThrowingAsyncEnumerable<AssistantQueryChunk>(
                new FeatureDisabledException(
                    "ai.intent-classification.disabled",
                    "Insights intent classification disabled.")));

        var client = _fixture.CreateAuthenticatedTenantClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var request = new { query = "q", subject = SampleMatterSubject };

        // Act
        var response = await client.PostAsJsonAsync("/api/insights/assistant/query", request);

        // Assert — 503 ProblemDetails, NOT SSE.
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json",
            "pre-stream kill-switch MUST return ProblemDetails — not text/event-stream — per ADR-032 ordering");

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("ai.intent-classification.disabled");
        raw.Should().NotContain("event: error",
            "no SSE body MUST be written when stream did not open");
        raw.Should().NotContain("[DONE]");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers — async-enumerable test fixtures
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build an <see cref="IAsyncEnumerable{T}"/> from a fixed sequence of chunks.
    /// </summary>
    private static async IAsyncEnumerable<AssistantQueryChunk> ToAsyncEnumerable(
        params AssistantQueryChunk[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
            await Task.Yield();
        }
    }

    /// <summary>
    /// Build an <see cref="IAsyncEnumerable{T}"/> that yields the supplied chunks then throws
    /// the supplied exception. Used to simulate mid-stream failure.
    /// </summary>
    private static async IAsyncEnumerable<AssistantQueryChunk> ThrowingAsyncEnumerable(
        AssistantQueryChunk firstChunk,
        Exception toThrow,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return firstChunk;
        await Task.Yield();
        throw toThrow;
    }

    /// <summary>
    /// Build an <see cref="IAsyncEnumerable{T}"/> that throws immediately on
    /// <c>MoveNextAsync</c> — before yielding any chunk. Used to simulate pre-stream
    /// failures (kill-switch tripped, ArgumentException).
    /// </summary>
    private static async IAsyncEnumerable<T> ImmediatelyThrowingAsyncEnumerable<T>(
        Exception toThrow,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        throw toThrow;
#pragma warning disable CS0162 // Unreachable code — required to make this a valid async iterator.
        yield break;
#pragma warning restore CS0162
    }

    /// <summary>
    /// Extract the <c>data:</c> JSON payloads of all SSE events with the supplied
    /// <c>event:</c> type from a raw SSE body. Used in assertions to verify event sequences
    /// without coupling to byte-for-byte formatting.
    /// </summary>
    private static List<string> ExtractEventsByType(string sseBody, string eventType)
    {
        var results = new List<string>();
        var lines = sseBody.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.Equals(line, $"event: {eventType}", StringComparison.Ordinal))
            {
                // Next line should be `data: {json}`.
                if (i + 1 < lines.Length)
                {
                    var dataLine = lines[i + 1].TrimEnd('\r');
                    const string prefix = "data: ";
                    if (dataLine.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        results.Add(dataLine[prefix.Length..]);
                    }
                }
            }
        }
        return results;
    }
}
