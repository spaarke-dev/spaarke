using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool wrapper that exposes the Insights Engine Assistant query endpoint
/// (<c>POST /api/insights/assistant/query</c>) as a single LLM-invokable function
/// (<see cref="InsightsQueryAsync"/>) on the <see cref="SprkChatAgent"/>.
///
/// <para>
/// <b>Zone B HTTP consumer (R5 CLAUDE.md §3.5 / §10 / refined ADR-013 §3.5)</b>:
/// this tool is the R5 chat-agent's HTTP consumer of the Insights endpoint. It
/// MUST NOT inject Insights-internal types (e.g., <c>IInsightsAi</c>,
/// <c>InsightsOrchestrator</c>, <c>AssistantToolCallHandler</c>,
/// <c>InsightsIntentClassifier</c>, <c>Models.Insights.*</c>). The endpoint
/// co-locates in the same BFF process today, but the boundary is binding —
/// same-process HTTP is the canonical Zone B consumption pattern. This file
/// intentionally contains <i>no</i> <c>using Sprk.Bff.Api.Services.Ai.Insights</c>
/// or <c>using Sprk.Bff.Api.Models.Insights</c> imports.
/// </para>
///
/// <para>
/// <b>Tool-routing scope (NFR-12 / UR-01 mitigation)</b>: the tool description
/// text is the load-bearing artifact for correct LLM tool-routing between this
/// tool and <see cref="InvokeSummarizePlaybookTool"/> (task 015). The
/// description scopes routing to <i>matter/project/invoice-scoped analytical
/// questions about entities</i> and explicitly differentiates from the
/// Summarize tool, which is for session-uploaded file summarization.
/// </para>
///
/// <para>
/// <b>Reuse statement (per R5 CLAUDE.md §3.1)</b>: this tool class is a THIN
/// HTTP delegating adapter. It composes EXISTING primitives — the typed
/// <see cref="HttpClient"/> registered via <c>IHttpClientFactory</c> in
/// <c>AnalysisServicesModule</c>, the existing <see cref="HttpContext"/> for
/// fresh bearer-token forwarding (ADR-028), and the existing
/// <see cref="SprkChatAgentFactory.ResolveTools"/> registration site. NO
/// parallel chat agent, NO parallel HTTP client, NO parallel tool framework,
/// NO new feature flag, NO new top-level DI lines. Registered via
/// <see cref="AIFunctionFactory.Create"/> — mirrors the canonical
/// <see cref="InvokeSummarizePlaybookTool"/> pattern (ADR-013 + ADR-010 +
/// AIPL-053).
/// </para>
///
/// <para>
/// <b>ADR-028 token discipline</b>: this tool does NOT capture or snapshot
/// bearer tokens. The bearer token is read FRESH per HTTP call from
/// <see cref="HttpContext.Request.Headers"/> Authorization header. Test 8
/// (<c>InvokeInsightsQueryToolTests.NoTokenSnapshot_...</c>) covers token
/// rotation mid-session.
/// </para>
///
/// <para>
/// <b>ADR-019 ProblemDetails error parsing</b>: non-2xx responses are parsed
/// as <c>application/problem+json</c>. The 12 stable <c>errorCode</c> values
/// from the integration brief §5.1 are preserved verbatim in the thrown
/// <see cref="InsightsToolException"/> so the frontend renderer (task 026) can
/// map per-code UX per integration brief column 4.
/// </para>
///
/// <para>
/// <b>Contract version</b>: v1.0 binding (POST /api/insights/assistant/query
/// shipped via Insights r2 Wave E3 task 042 / PR #337). v1.1 forward-compat:
/// the response is deserialized loosely (<see cref="JsonElement"/> for
/// <c>structuredResult</c> + extension-data passthrough on the outer envelope)
/// so v1.1 additions (e.g., <c>citations[].href</c>) flow through to the
/// renderer (task 026) without code changes. The v1.1 SSE opt-in sets
/// <c>Accept: text/event-stream</c>; on 406 it gracefully falls back to v1.0
/// single-shot JSON.
/// </para>
///
/// <para>
/// <b>Instantiated by</b> <see cref="SprkChatAgentFactory"/> per session, NOT
/// DI-registered as a standalone tool class (per ADR-010 + AIPL-053 — same
/// pattern as <see cref="InvokeSummarizePlaybookTool"/>). The typed
/// <see cref="HttpClient"/> dependency IS DI-registered via
/// <c>services.AddHttpClient&lt;InvokeInsightsQueryTool&gt;(...)</c> inside
/// <c>AnalysisServicesModule.AddAnalysisOrchestrationServices</c> — ONE line,
/// inside the feature module, ZERO new <c>Program.cs</c> lines.
/// </para>
/// </summary>
public sealed class InvokeInsightsQueryTool
{
    /// <summary>The AIFunction name exposed to the LLM tool-schema.
    /// Lower-dot-separated to match the binding contract v1.0 §3.1 tool name.</summary>
    public const string ToolName = "insights.query";

    /// <summary>
    /// Description string passed to <see cref="AIFunctionFactory.Create"/>. This is the
    /// single LOAD-BEARING artifact for LLM tool routing (NFR-12 / UR-01 mitigation). It
    /// scopes routing to matter/project/invoice-scoped analytical questions about
    /// entities and explicitly differentiates from
    /// <see cref="InvokeSummarizePlaybookTool"/> (file-scoped / session-scoped) so the
    /// LLM picks the right tool for each natural-language prompt.
    /// </summary>
    public const string ToolDescription =
        "Answer matter/project/invoice-scoped analytical questions about entities. " +
        "Use for questions about a specific matter, project, or invoice (predicted cost, " +
        "key dates, closing conditions, outstanding amounts, status updates, comparable " +
        "outcomes, etc.). The tool routes server-side between a predictive playbook path " +
        "(structured answer) and a citation-grounded RAG path (cited prose). " +
        "Do NOT use this tool for summarizing files uploaded to the current chat session " +
        "(use invoke_summarize_playbook for that). Do NOT use for free-form web research " +
        "or text refinement.";

    /// <summary>Relative endpoint path for the Insights Assistant query.</summary>
    public const string EndpointPath = "/api/insights/assistant/query";

    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<InvokeInsightsQueryTool> _logger;

    /// <summary>
    /// Constructs the agent-tool wrapper.
    /// </summary>
    /// <param name="httpClient">
    /// Typed <see cref="HttpClient"/> registered via
    /// <c>services.AddHttpClient&lt;InvokeInsightsQueryTool&gt;(...)</c> in
    /// <c>AnalysisServicesModule</c>. The BaseAddress is configured from
    /// <c>Bff:BaseAddress</c>. Default Accept header is <c>application/json</c>; this
    /// tool additionally requests <c>text/event-stream</c> per call for v1.1 SSE
    /// opt-in (graceful fallback on 406 to v1.0 JSON).
    /// </param>
    /// <param name="httpContextAccessor">
    /// Optional accessor for the ambient <see cref="HttpContext"/>. Used to forward
    /// the fresh OBO bearer token per call (ADR-028; NEVER snapshotted in the
    /// constructor). Null is acceptable for unit tests; production registration in
    /// <c>AnalysisServicesModule</c> ensures <see cref="IHttpContextAccessor"/> is
    /// available.
    /// </param>
    /// <param name="logger">Logger for diagnostic and telemetry events.</param>
    public InvokeInsightsQueryTool(
        HttpClient httpClient,
        IHttpContextAccessor? httpContextAccessor,
        ILogger<InvokeInsightsQueryTool> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _httpContextAccessor = httpContextAccessor;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// LLM-invokable Insights query tool function. POSTs to
    /// <c>/api/insights/assistant/query</c> per binding contract v1.0 §3.1 + v1.1
    /// forward-compat (SSE opt-in via <c>Accept: text/event-stream</c>).
    /// </summary>
    /// <param name="query">
    /// The user's natural-language question (1..500 chars). Required. The tool does NOT
    /// pre-validate length — the BFF returns 400 <c>query.required</c> for empty or
    /// whitespace input.
    /// </param>
    /// <param name="subject">
    /// The scope entity in the format <c>&lt;scheme&gt;:&lt;guid&gt;</c> where scheme is
    /// <c>matter</c>, <c>project</c>, or <c>invoice</c>. Resolved from the active chat
    /// host context (task 025 — subject resolution). Required. The tool does NOT
    /// pre-validate format — the BFF returns 400 <c>subject.invalid</c> for malformed
    /// values.
    /// </param>
    /// <param name="forceMode">
    /// Optional intent override. Set to <c>"playbook"</c> or <c>"rag"</c> when invoking
    /// via an explicit slash command (task 019 <c>/ask-insights playbook ...</c> /
    /// <c>/ask-insights rag ...</c>). Omit for natural-language tool-calls — the BFF
    /// intent classifier will route automatically per contract §3.2.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A JSON string carrying the full v1.0 (or v1.1) Insights response envelope —
    /// <c>{ path, answer, citations, confidence, playbookId, structuredResult,
    /// diagnostics, ... }</c>. Forwarded verbatim to the frontend renderer (task 026)
    /// so the renderer can branch on <c>path</c> ("playbook" vs "rag") for two-path
    /// UX. Unknown fields (e.g., v1.1 <c>citations[].href</c>) pass through
    /// unchanged via <see cref="JsonElement"/> + extension-data deserialization.
    /// </returns>
    /// <exception cref="InsightsToolException">
    /// Thrown on non-2xx responses. Preserves <c>errorCode</c>,
    /// <c>correlationId</c>, <c>status</c>, <c>title</c>, and <c>detail</c> per
    /// ADR-019. The 12 contract error codes (integration brief §5.1) are surfaced
    /// verbatim; synthetic codes <c>auth.401</c> (no errorCode on 401) and
    /// <c>rate-limit.429</c> (no errorCode on 429) are used as fallbacks.
    /// </exception>
    [Description(ToolDescription)]
    public async Task<string> InsightsQueryAsync(
        [Description("The user's natural-language question (1..500 chars). Required.")]
        string query,
        [Description("The scope entity in the format '<scheme>:<guid>' where scheme is " +
                     "'matter', 'project', or 'invoice'. Resolved from the active chat host " +
                     "context. Required.")]
        string subject,
        [Description("Optional intent override. Set to 'playbook' or 'rag' when invoking " +
                     "via an explicit slash command. Omit for natural-language tool-calls — " +
                     "the BFF intent classifier will route automatically.")]
        string? forceMode = null,
        CancellationToken cancellationToken = default)
    {
        // Correlation ID propagation (spec FR-17 / SC-16). Prefer the ambient Activity ID
        // (set by ASP.NET Core diagnostic source) so a single ID stitches end-to-end across
        // BFF process boundaries; fall back to a new GUID if no ambient activity exists.
        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");

        // Build the request payload per integration brief §3 (request schema). Use a
        // PRIVATE local record — does NOT import InsightsAssistantQueryRequest from
        // Models/Insights/ (Zone B boundary discipline per R5 CLAUDE.md §10).
        var payload = new InsightsQueryRequest(
            Query: query,
            Subject: subject,
            ForceMode: forceMode,
            ConversationContext: null); // Phase 1.5 telemetry only; R5 wires later if needed.

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointPath)
        {
            Content = JsonContent.Create(payload, options: SerializerOptions),
        };

        // v1.1 SSE opt-in via Accept header (per v1.1 contract §2.1). Server returns 406
        // if SSE not yet supported — we treat that as graceful fallback to v1.0 single-shot
        // JSON and reissue the request without the SSE preference.
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Correlation header (FR-17 / SC-16).
        request.Headers.TryAddWithoutValidation("x-correlation-id", correlationId);

        // OBO bearer-token forwarding — read FRESH per call per ADR-028 (NEVER snapshot
        // in constructor). The ambient HttpContext is available when the chat agent runs
        // inside a request scope; for non-request-scoped invocations (e.g., background
        // worker), we proceed without a token and let the BFF return 401.
        ForwardBearerToken(request);

        _logger.LogInformation(
            "InvokeInsightsQueryTool: dispatching to {EndpointPath} (subject={Subject} " +
            "forceMode={ForceMode} correlationId={CorrelationId})",
            EndpointPath, subject, forceMode ?? "(absent)", correlationId);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "InvokeInsightsQueryTool: HTTP send failed (correlationId={CorrelationId})",
                correlationId);
            throw new InsightsToolException(
                errorCode: "INSIGHTS_ASSISTANT_INTERNAL_ERROR",
                status: 500,
                title: "Insights call failed",
                detail: $"HTTP transport error: {ex.Message}",
                correlationId: correlationId,
                innerException: ex);
        }

        try
        {
            // v1.1 SSE opt-in graceful fallback: 406 Not Acceptable means the server did
            // not honor text/event-stream (v1.0 only). Reissue without the SSE preference.
            if (response.StatusCode == System.Net.HttpStatusCode.NotAcceptable)
            {
                response.Dispose();
                using var jsonOnly = new HttpRequestMessage(HttpMethod.Post, EndpointPath)
                {
                    Content = JsonContent.Create(payload, options: SerializerOptions),
                };
                jsonOnly.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                jsonOnly.Headers.TryAddWithoutValidation("x-correlation-id", correlationId);
                ForwardBearerToken(jsonOnly);

                response = await _httpClient
                    .SendAsync(jsonOnly, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Telemetry — observability headers per contract §4.2.
            EmitTelemetryTags(response, correlationId);

            if (response.IsSuccessStatusCode)
            {
                // Loose deserialization for v1.1 forward-compat: read the body as a string
                // and let the frontend renderer (task 026) own the parse. This preserves
                // unknown fields verbatim per contract v1.1 §0a (forward-compatible
                // additive changes; v1.0 clients ignore unknowns).
                var body = await response.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                return body;
            }

            // Non-2xx — parse ProblemDetails per ADR-019. The 12 stable errorCode values
            // from integration brief §5.1 are preserved verbatim; 401/429 without
            // errorCode get synthetic codes for renderer-side mapping.
            await ThrowInsightsToolExceptionAsync(response, correlationId, cancellationToken)
                .ConfigureAwait(false);
            // Unreachable — ThrowInsightsToolExceptionAsync always throws.
            throw new InvalidOperationException("Unreachable.");
        }
        finally
        {
            response.Dispose();
        }
    }

    /// <summary>
    /// Returns the single <see cref="AIFunction"/> for this tool, registered under
    /// <see cref="ToolName"/> with <see cref="ToolDescription"/>. Called by
    /// <see cref="SprkChatAgentFactory.ResolveTools"/>.
    /// </summary>
    public IEnumerable<AIFunction> GetTools()
    {
        yield return AIFunctionFactory.Create(
            InsightsQueryAsync,
            name: ToolName,
            description: ToolDescription);
    }

    // ── Private helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Forwards the OBO bearer token from the ambient <see cref="HttpContext"/> to the
    /// outbound request. Read FRESH per call per ADR-028 — never snapshotted in
    /// constructor or captured in a closure.
    /// </summary>
    private void ForwardBearerToken(HttpRequestMessage request)
    {
        var ctx = _httpContextAccessor?.HttpContext;
        if (ctx is null)
        {
            // No ambient HttpContext (e.g., unit test or background-worker invocation).
            // Proceed without a token and let the BFF return 401.
            return;
        }

        var authHeader = ctx.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            return;
        }

        // The header value already includes the "Bearer " prefix when set by ASP.NET
        // Core middleware; preserve it verbatim.
        request.Headers.TryAddWithoutValidation("Authorization", authHeader);
    }

    /// <summary>
    /// Emits telemetry tags onto the ambient <see cref="Activity"/> (when present) for
    /// end-to-end correlation in App Insights / Kusto per FR-17 / FR-18. Reads the
    /// contract §4.2 observability headers (<c>X-Insights-Path</c>,
    /// <c>X-Insights-Elapsed-Ms</c>, <c>X-Insights-Cache</c>) for path-aware analysis.
    /// </summary>
    private static void EmitTelemetryTags(HttpResponseMessage response, string correlationId)
    {
        var activity = Activity.Current;
        if (activity is null) return;

        activity.SetTag("insights.correlationId", correlationId);
        if (response.Headers.TryGetValues("X-Insights-Path", out var pathValues))
        {
            activity.SetTag("insights.path", string.Join(",", pathValues));
        }
        if (response.Headers.TryGetValues("X-Insights-Elapsed-Ms", out var elapsedValues))
        {
            activity.SetTag("insights.elapsedMs", string.Join(",", elapsedValues));
        }
        if (response.Headers.TryGetValues("X-Insights-Cache", out var cacheValues))
        {
            activity.SetTag("insights.cacheHit", string.Join(",", cacheValues));
        }
    }

    /// <summary>
    /// Parses a non-2xx response as ProblemDetails per ADR-019 and throws an
    /// <see cref="InsightsToolException"/> preserving <c>errorCode</c>,
    /// <c>correlationId</c>, <c>status</c>, <c>title</c>, and <c>detail</c>. Handles
    /// 401 (no errorCode → synthetic <c>auth.401</c>) and 429 (no errorCode →
    /// synthetic <c>rate-limit.429</c> with <c>Retry-After</c>) per integration brief
    /// §5.1.
    /// </summary>
    private static async Task ThrowInsightsToolExceptionAsync(
        HttpResponseMessage response,
        string fallbackCorrelationId,
        CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        string? body = null;
        try
        {
            body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Body unreadable — proceed with status-derived synthetic error.
        }

        string errorCode;
        string title;
        string detail;
        string correlationId = fallbackCorrelationId;
        string? retryAfter = null;

        // Try to parse ProblemDetails body.
        var parsed = TryParseProblemDetails(body);
        if (parsed is not null)
        {
            errorCode = parsed.ErrorCode ?? SyntheticErrorCodeForStatus(status);
            title = parsed.Title ?? response.ReasonPhrase ?? "Insights call failed";
            detail = parsed.Detail ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(parsed.CorrelationId))
            {
                correlationId = parsed.CorrelationId!;
            }
        }
        else
        {
            errorCode = SyntheticErrorCodeForStatus(status);
            title = response.ReasonPhrase ?? "Insights call failed";
            detail = body ?? string.Empty;
        }

        if (status == 429 && response.Headers.RetryAfter is { } retryAfterHeader)
        {
            retryAfter = retryAfterHeader.ToString();
        }

        throw new InsightsToolException(
            errorCode: errorCode,
            status: status,
            title: title,
            detail: detail,
            correlationId: correlationId,
            retryAfter: retryAfter);
    }

    private static string SyntheticErrorCodeForStatus(int status) => status switch
    {
        401 => "auth.401",
        429 => "rate-limit.429",
        _ => "INSIGHTS_ASSISTANT_INTERNAL_ERROR",
    };

    private static ProblemDetailsPayload? TryParseProblemDetails(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            return JsonSerializer.Deserialize<ProblemDetailsPayload>(body, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Local DTOs (Zone B boundary — do NOT use Models/Insights/) ───────────────

    /// <summary>
    /// Private request DTO mirroring the contract v1.0 §3 schema. Local definition
    /// (NOT imported from <c>Models/Insights/InsightsAssistantQueryRequest</c>) to
    /// preserve the Zone B boundary per R5 CLAUDE.md §10.
    /// </summary>
    private sealed record InsightsQueryRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("forceMode")] string? ForceMode,
        [property: JsonPropertyName("conversationContext")] InsightsConversationContext? ConversationContext);

    /// <summary>Local conversation-context record (Zone B boundary).</summary>
    private sealed record InsightsConversationContext(
        [property: JsonPropertyName("conversationId")] string? ConversationId,
        [property: JsonPropertyName("previousTurnSummary")] string? PreviousTurnSummary);

    /// <summary>
    /// Local ProblemDetails payload (ADR-019). Extension fields (<c>errorCode</c>,
    /// <c>correlationId</c>) are first-class properties; standard fields use
    /// nullable strings/int for resilient parsing.
    /// </summary>
    internal sealed record ProblemDetailsPayload(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("status")] int? Status,
        [property: JsonPropertyName("detail")] string? Detail,
        [property: JsonPropertyName("instance")] string? Instance,
        [property: JsonPropertyName("errorCode")] string? ErrorCode,
        [property: JsonPropertyName("correlationId")] string? CorrelationId);
}

/// <summary>
/// Structured tool exception preserving the contract v1.0 §5.1 error envelope:
/// stable <c>errorCode</c>, HTTP <c>status</c>, ProblemDetails <c>title</c> and
/// <c>detail</c>, and <c>correlationId</c> for ops/support lookup. Surfaced to the
/// frontend renderer (task 026) so per-code UX maps from integration brief column 4.
/// </summary>
/// <remarks>
/// <para>
/// The exception NEVER includes document content, prompt text, LLM raw output, or
/// stack traces in its public fields (per ADR-018 + ADR-019 information-leakage
/// constraints). The <see cref="CorrelationId"/> is the ops log-lookup key.
/// </para>
/// </remarks>
public sealed class InsightsToolException : Exception
{
    /// <summary>
    /// Stable error code from the contract v1.0 §5.1 matrix (e.g.,
    /// <c>query.required</c>, <c>subject.invalid</c>, <c>ai.insights.disabled</c>)
    /// or a synthetic code (<c>auth.401</c>, <c>rate-limit.429</c>,
    /// <c>INSIGHTS_ASSISTANT_INTERNAL_ERROR</c>) when ProblemDetails carries no
    /// errorCode extension.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>HTTP status code from the response.</summary>
    public int Status { get; }

    /// <summary>ProblemDetails <c>title</c> field (short title).</summary>
    public string Title { get; }

    /// <summary>ProblemDetails <c>detail</c> field (human-readable detail).</summary>
    public string Detail { get; }

    /// <summary>
    /// Correlation ID for cross-service log lookup (App Insights / Kusto).
    /// Sourced from ProblemDetails <c>correlationId</c> extension when present;
    /// falls back to the client-generated <c>x-correlation-id</c> header value.
    /// </summary>
    public string CorrelationId { get; }

    /// <summary>
    /// <c>Retry-After</c> header value for 429 responses (per ADR-016 rate-limit
    /// honoring). Null for non-429 errors.
    /// </summary>
    public string? RetryAfter { get; }

    public InsightsToolException(
        string errorCode,
        int status,
        string title,
        string detail,
        string correlationId,
        string? retryAfter = null,
        Exception? innerException = null)
        : base($"Insights query failed: {errorCode} (HTTP {status}): {title}", innerException)
    {
        ErrorCode = errorCode ?? throw new ArgumentNullException(nameof(errorCode));
        Status = status;
        Title = title ?? string.Empty;
        Detail = detail ?? string.Empty;
        CorrelationId = correlationId ?? throw new ArgumentNullException(nameof(correlationId));
        RetryAfter = retryAfter;
    }
}
