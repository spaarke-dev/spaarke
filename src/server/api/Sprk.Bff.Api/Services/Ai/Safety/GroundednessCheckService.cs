using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// Calls the Azure AI Content Safety Groundedness Detection API after each LLM response
/// to identify claims that are not supported by the retrieved source documents (RAG passages).
///
/// Design decisions:
///   - POST-LLM, non-blocking: runs after the response stream completes and the result is
///     emitted as a <c>safety_annotation</c> SSE event. The user sees the AI response
///     immediately; ungrounded segments are annotated after.
///   - Fail-open: HTTP 429, 5xx, and timeouts all return <see cref="GroundednessResult.AssumeGrounded"/>
///     with a warning log. The check MUST NOT suppress a valid AI response.
///   - Skip on empty sources: when no RAG passages were retrieved there is nothing to check
///     against; the API call is skipped and IsGrounded=true is returned immediately.
///   - ADR-015 compliance: response text and document content MUST NOT appear in logs.
///     Only segment count, outcome, and latency are logged and emitted as OTEL metrics.
///
/// Named HttpClient: registered in <see cref="Infrastructure.DI.AiSafetyModule"/> with the
/// Content Safety base address. Shared with PromptShieldService.
///
/// Endpoint called: POST {endpoint}/contentsafety/text:detectGroundedness?api-version=2024-09-15-preview
///
/// Lifetime: Scoped — one instance per request, consistent with the chat request pipeline.
/// </summary>
public sealed class GroundednessCheckService : IGroundednessCheckService
{
    // -------------------------------------------------------------------------
    // API constants
    // -------------------------------------------------------------------------

    /// <summary>
    /// Relative path + query for the Groundedness Detection endpoint.
    /// Base address is the Content Safety endpoint (https://{resource}.cognitiveservices.azure.com/).
    /// </summary>
    private const string GroundednessPath =
        "contentsafety/text:detectGroundedness?api-version=2024-09-15-preview";

    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly HttpClient _httpClient;
    private readonly ILogger<GroundednessCheckService> _logger;
    private readonly GroundednessCheckTelemetry _telemetry;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GroundednessCheckService(
        HttpClient httpClient,
        ILogger<GroundednessCheckService> logger,
        GroundednessCheckTelemetry telemetry)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    // -------------------------------------------------------------------------
    // IGroundednessCheckService
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<GroundednessResult> CheckAsync(
        GroundednessRequest request,
        CancellationToken ct = default)
    {
        // Fast-path: no sources → nothing to check; skip the API call entirely.
        if (request.SourceDocuments.Count == 0)
        {
            _logger.LogDebug(
                "Groundedness check skipped: no source documents provided. Returning assumed grounded.");

            _telemetry.RecordCheck(
                isGrounded: true,
                ungroundedSegmentCount: 0,
                latencyMs: 0,
                outcome: GroundednessCheckTelemetry.OutcomeSkipped);

            return GroundednessResult.AssumeGrounded(latencyMs: 0);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var apiResponse = await CallGroundednessApiAsync(request, ct).ConfigureAwait(false);
            sw.Stop();

            var latencyMs = sw.Elapsed.TotalMilliseconds;

            if (apiResponse is null)
            {
                // Null from a deserialization failure — treat as fail-open.
                _logger.LogWarning(
                    "Groundedness check returned null response. Returning assumed grounded. " +
                    "LatencyMs={LatencyMs:F1}",
                    latencyMs);

                _telemetry.RecordCheck(
                    isGrounded: true,
                    ungroundedSegmentCount: 0,
                    latencyMs: latencyMs,
                    outcome: GroundednessCheckTelemetry.OutcomeFailOpen);

                return GroundednessResult.AssumeGrounded(latencyMs);
            }

            return BuildResult(apiResponse, latencyMs);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout (not caller cancellation) — fail-open, log warning.
            sw.Stop();
            var latencyMs = sw.Elapsed.TotalMilliseconds;

            _logger.LogWarning(
                "Groundedness check timed out after {LatencyMs:F1}ms. Returning assumed grounded.",
                latencyMs);

            _telemetry.RecordCheck(
                isGrounded: true,
                ungroundedSegmentCount: 0,
                latencyMs: latencyMs,
                outcome: GroundednessCheckTelemetry.OutcomeFailOpen);

            return GroundednessResult.AssumeGrounded(latencyMs);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            var latencyMs = sw.Elapsed.TotalMilliseconds;

            _logger.LogWarning(
                ex,
                "Groundedness check HTTP error (StatusCode={StatusCode}). " +
                "Returning assumed grounded. LatencyMs={LatencyMs:F1}",
                ex.StatusCode,
                latencyMs);

            _telemetry.RecordCheck(
                isGrounded: true,
                ungroundedSegmentCount: 0,
                latencyMs: latencyMs,
                outcome: GroundednessCheckTelemetry.OutcomeFailOpen);

            return GroundednessResult.AssumeGrounded(latencyMs);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var latencyMs = sw.Elapsed.TotalMilliseconds;

            _logger.LogWarning(
                ex,
                "Groundedness check failed unexpectedly. Returning assumed grounded. " +
                "LatencyMs={LatencyMs:F1}",
                latencyMs);

            _telemetry.RecordCheck(
                isGrounded: true,
                ungroundedSegmentCount: 0,
                latencyMs: latencyMs,
                outcome: GroundednessCheckTelemetry.OutcomeFailOpen);

            return GroundednessResult.AssumeGrounded(latencyMs);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calls POST contentsafety/text:detectGroundedness and returns the deserialized response.
    /// Throws <see cref="HttpRequestException"/> for 4xx/5xx responses (including 429).
    /// </summary>
    private async Task<GroundednessApiResponse?> CallGroundednessApiAsync(
        GroundednessRequest request,
        CancellationToken ct)
    {
        var body = new GroundednessApiRequest
        {
            Domain = "Generic",
            Task = "QnA",
            Qna = new QnaPayload
            {
                Query = request.Query ?? string.Empty,
                Answer = request.LlmResponse,
            },
            GroundingSources = request.SourceDocuments,
            Reasoning = false,  // Omit reasoning to minimise latency (target <200ms).
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, GroundednessPath)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };

        using var response = await _httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);

        // Surface 4xx/5xx as HttpRequestException (including 429 Rate Limit → fail-open upstream).
        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<GroundednessApiResponse>(JsonOptions, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Converts a successful API response into a <see cref="GroundednessResult"/>
    /// and emits OTEL metrics.
    /// </summary>
    private GroundednessResult BuildResult(GroundednessApiResponse apiResponse, double latencyMs)
    {
        var ungroundedDetected = apiResponse.UngroundedDetected;
        var details = apiResponse.UngroundedDetails ?? [];

        var segments = details
            .Where(d => !string.IsNullOrEmpty(d.Text))
            .Select(d => new UngroundedSegment(d.Text!, d.Reason))
            .ToList();

        if (ungroundedDetected && segments.Count > 0)
        {
            // ADR-015: log count + latency only — NEVER log segment text or document content.
            _logger.LogWarning(
                "Groundedness check: ungrounded segments detected. " +
                "SegmentCount={SegmentCount}, LatencyMs={LatencyMs:F1}",
                segments.Count,
                latencyMs);

            _telemetry.RecordCheck(
                isGrounded: false,
                ungroundedSegmentCount: segments.Count,
                latencyMs: latencyMs,
                outcome: GroundednessCheckTelemetry.OutcomeUngrounded);

            return GroundednessResult.Ungrounded(segments, latencyMs);
        }

        // ADR-015: log latency only.
        _logger.LogInformation(
            "Groundedness check: response is grounded. LatencyMs={LatencyMs:F1}",
            latencyMs);

        _telemetry.RecordCheck(
            isGrounded: true,
            ungroundedSegmentCount: 0,
            latencyMs: latencyMs,
            outcome: GroundednessCheckTelemetry.OutcomeGrounded);

        return GroundednessResult.Grounded(latencyMs);
    }

    // =========================================================================
    // API request / response DTOs
    // (internal — not part of the public contract, not logged per ADR-015)
    // =========================================================================

    private sealed class GroundednessApiRequest
    {
        [JsonPropertyName("domain")]
        public string Domain { get; init; } = "Generic";

        [JsonPropertyName("task")]
        public string Task { get; init; } = "QnA";

        [JsonPropertyName("qna")]
        public QnaPayload? Qna { get; init; }

        [JsonPropertyName("groundingSources")]
        public IReadOnlyList<string> GroundingSources { get; init; } = [];

        [JsonPropertyName("reasoning")]
        public bool Reasoning { get; init; }
    }

    private sealed class QnaPayload
    {
        [JsonPropertyName("query")]
        public string Query { get; init; } = string.Empty;

        [JsonPropertyName("answer")]
        public string Answer { get; init; } = string.Empty;
    }

    private sealed class GroundednessApiResponse
    {
        [JsonPropertyName("ungroundedDetected")]
        public bool UngroundedDetected { get; init; }

        [JsonPropertyName("ungroundedPercentage")]
        public double? UngroundedPercentage { get; init; }

        [JsonPropertyName("ungroundedDetails")]
        public IReadOnlyList<UngroundedDetail>? UngroundedDetails { get; init; }
    }

    private sealed class UngroundedDetail
    {
        /// <summary>Verbatim text of the ungrounded segment. ADR-015: MUST NOT be logged.</summary>
        [JsonPropertyName("text")]
        public string? Text { get; init; }

        /// <summary>Reason the segment is ungrounded (only populated when reasoning=true).</summary>
        [JsonPropertyName("reason")]
        public string? Reason { get; init; }
    }
}
