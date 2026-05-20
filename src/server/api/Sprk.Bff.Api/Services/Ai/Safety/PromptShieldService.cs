using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// Calls the Azure AI Content Safety Prompt Shields REST API to detect prompt injection attacks.
///
/// API reference: POST {endpoint}/contentsafety/text:shieldPrompt?api-version=2024-09-01
///
/// Failure modes (all fail-open to preserve availability):
///   - HTTP 429 (throttled)  → log warning, return <see cref="PromptShieldResult.FailOpen"/>
///   - HTTP 5xx (server err) → log warning, return <see cref="PromptShieldResult.FailOpen"/>
///   - Timeout (&gt;100ms)   → log warning, return <see cref="PromptShieldResult.FailOpen"/>
///   - Network error         → log warning, return <see cref="PromptShieldResult.FailOpen"/>
///
/// ADR-015: prompt content and document text are NEVER logged.
/// Only identifiers (document count, user message character count), outcome, and timing are logged.
/// </summary>
public sealed class PromptShieldService : IPromptShieldService
{
    /// <summary>Named HttpClient registered in <see cref="Infrastructure.DI.AiSafetyModule"/>.</summary>
    public const string HttpClientName = "ContentSafety";

    private const string ApiPath = "contentsafety/text:shieldPrompt";
    private const string ApiVersion = "2024-09-01";

    /// <summary>
    /// Hard deadline for the Content Safety call within the streaming first-token budget.
    /// Matches the 100ms P95 target from the task spec and <see cref="PromptShieldTelemetry"/>.
    /// </summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly PromptShieldTelemetry _telemetry;
    private readonly ILogger<PromptShieldService> _logger;

    public PromptShieldService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        PromptShieldTelemetry telemetry,
        ILogger<PromptShieldService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PromptShieldResult> ScanAsync(PromptShieldRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // Combine the caller's cancellation token with our hard 100ms deadline.
        using var timeoutCts = new CancellationTokenSource(CallTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var result = await CallApiAsync(request, linked.Token).ConfigureAwait(false);
            sw.Stop();

            var latencyMs = sw.Elapsed.TotalMilliseconds;
            result = result with { LatencyMs = latencyMs };

            // ADR-015: log only outcome + counts, never prompt text.
            _logger.LogInformation(
                "PromptShield scan complete: isBlocked={IsBlocked}, reason={BlockReason}, " +
                "docCount={DocCount}, latencyMs={LatencyMs:F1}",
                result.IsBlocked, result.BlockReason,
                request.Documents?.Count ?? 0, latencyMs);

            _telemetry.RecordScan(result, latencyMs);
            return result;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            sw.Stop();
            var latencyMs = sw.Elapsed.TotalMilliseconds;

            _logger.LogWarning(
                "PromptShield scan timed out after {LatencyMs:F1}ms (limit={LimitMs}ms). " +
                "Failing open — request will proceed to LLM. docCount={DocCount}",
                latencyMs, CallTimeout.TotalMilliseconds, request.Documents?.Count ?? 0);

            _telemetry.RecordFailOpen("timeout", latencyMs);
            return PromptShieldResult.FailOpen(latencyMs);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var latencyMs = sw.Elapsed.TotalMilliseconds;

            _logger.LogWarning(ex,
                "PromptShield scan failed after {LatencyMs:F1}ms. " +
                "Failing open — request will proceed to LLM. docCount={DocCount}",
                latencyMs, request.Documents?.Count ?? 0);

            _telemetry.RecordFailOpen("error", latencyMs);
            return PromptShieldResult.FailOpen(latencyMs);
        }
    }

    // =========================================================================
    // Private: API call
    // =========================================================================

    private async Task<PromptShieldResult> CallApiAsync(PromptShieldRequest request, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        // Read API key at call time (supports Key Vault dynamic secret rotation).
        var apiKey = _configuration["AiSafety:ContentSafety:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning(
                "PromptShield: AiSafety:ContentSafety:ApiKey is not configured. Failing open.");
            return PromptShieldResult.FailOpen(0);
        }

        var requestBody = BuildRequestBody(request);
        var requestUrl = $"{ApiPath}?api-version={ApiVersion}";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        httpRequest.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(requestBody, SerializerOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(httpRequest, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning(
                "PromptShield: Content Safety API returned 429 (rate limited). Failing open.");
            return PromptShieldResult.FailOpen(0);
        }

        if ((int)response.StatusCode >= 500)
        {
            _logger.LogWarning(
                "PromptShield: Content Safety API returned HTTP {StatusCode}. Failing open.",
                (int)response.StatusCode);
            return PromptShieldResult.FailOpen(0);
        }

        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseResponse(responseBody, request);
    }

    // =========================================================================
    // Private: request / response mapping
    // =========================================================================

    private static PromptShieldApiRequest BuildRequestBody(PromptShieldRequest request)
    {
        var documents = request.Documents is { Count: > 0 }
            ? request.Documents.Select(d => new PromptShieldDocument(d)).ToList()
            : null;

        return new PromptShieldApiRequest(request.UserMessage, documents);
    }

    private static PromptShieldResult ParseResponse(string json, PromptShieldRequest request)
    {
        var response = JsonSerializer.Deserialize<PromptShieldApiResponse>(json, SerializerOptions);
        if (response is null)
        {
            // Unparseable response — fail open.
            return PromptShieldResult.FailOpen(0);
        }

        // --- Check user prompt attack ---
        if (response.UserPromptAnalysis?.AttackDetected == true)
        {
            return new PromptShieldResult(
                IsBlocked: true,
                BlockReason: PromptShieldBlockReason.UserInjection,
                DetectedAttackType: "UserPromptAttack",
                BlockedDocumentIndexes: [],
                LatencyMs: 0);
        }

        // --- Check document attacks ---
        var blockedIndexes = new List<int>();
        if (response.DocumentsAnalysis is { Count: > 0 })
        {
            for (int i = 0; i < response.DocumentsAnalysis.Count; i++)
            {
                if (response.DocumentsAnalysis[i].AttackDetected == true)
                {
                    blockedIndexes.Add(i);
                }
            }
        }

        if (blockedIndexes.Count > 0)
        {
            return new PromptShieldResult(
                IsBlocked: true,
                BlockReason: PromptShieldBlockReason.DocumentInjection,
                DetectedAttackType: "DocumentAttack",
                BlockedDocumentIndexes: blockedIndexes.AsReadOnly(),
                LatencyMs: 0);
        }

        return PromptShieldResult.Safe(0);
    }

    // =========================================================================
    // Private: API contract DTOs (internal, not part of the public surface)
    // =========================================================================

    private sealed record PromptShieldApiRequest(
        [property: JsonPropertyName("userPrompt")] string UserPrompt,
        [property: JsonPropertyName("documents")] List<PromptShieldDocument>? Documents);

    private sealed record PromptShieldDocument(
        [property: JsonPropertyName("text")] string Text);

    private sealed class PromptShieldApiResponse
    {
        [JsonPropertyName("userPromptAnalysis")]
        public PromptShieldAnalysis? UserPromptAnalysis { get; init; }

        [JsonPropertyName("documentsAnalysis")]
        public List<PromptShieldAnalysis>? DocumentsAnalysis { get; init; }
    }

    private sealed class PromptShieldAnalysis
    {
        [JsonPropertyName("attackDetected")]
        public bool? AttackDetected { get; init; }
    }
}
