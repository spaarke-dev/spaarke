using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Foundry;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat- and playbook-invocable typed handler that performs legal research via Azure AI
/// Foundry Bing Grounding (R6 Wave 8 — Q9 chat-tool migration). Replaces the legacy
/// hardcoded <c>LegalResearchTools</c> class previously instantiated in
/// <c>SprkChatAgentFactory.ResolveTools</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One handler, two methods</strong>: two <c>sprk_analysistool</c> rows
/// (<c>LEGAL-RESEARCH</c> and <c>LEGAL-CASE-LOOKUP</c>) share this single
/// <c>sprk_handlerclass = LegalResearchHandler</c>. Each row's
/// <c>sprk_configuration.method</c> selects the internal method. Mirrors the pre-R6
/// exposure model that hand-registered two <c>AIFunction</c>s from one
/// <c>LegalResearchTools</c> instance.
/// </para>
/// <list type="bullet">
/// <item><c>method = "ResearchLegal"</c> — broad legal topic / doctrine / statute research via
/// Bing Grounding. LLM input field: <c>topic</c>.</item>
/// <item><c>method = "LookupCase"</c> — specific case citation lookup via Bing Grounding,
/// anchored to law.justia.com / courtlistener.com / scholar.google.com. LLM input field:
/// <c>citation</c>.</item>
/// </list>
/// <para>
/// <strong>Capability gate (R6 Wave 7b infrastructure)</strong>: the corresponding two
/// <c>sprk_analysistool</c> rows set <c>sprk_requiredcapability = "legal_research"</c>
/// (= <see cref="PlaybookCapabilities.LegalResearch"/>). The data-driven block in
/// <c>SprkChatAgentFactory.ResolveTools</c> applies <c>IsCapabilityGateSatisfied</c> at
/// session start and silently withholds this handler when the playbook's capability set
/// lacks the value. Standalone chat (no playbook, capabilities = <c>CoreCapabilities</c>)
/// does NOT include <c>legal_research</c>, so this handler is unreachable from standalone
/// chat — preserving the pre-Wave-8 security boundary enforced by the hardcoded
/// <c>if (capabilities.Contains(PlaybookCapabilities.LegalResearch))</c> block that this
/// migration removes.
/// </para>
/// <para>
/// <strong>ADR-015 PII sanitization (BINDING)</strong>: legal queries may carry client
/// names, matter references, email addresses, and other PII. <see cref="QuerySanitizer"/>
/// is invoked BEFORE every Bing Grounding call. Sanitization is enforced inside each
/// per-method execution path; tests assert sanitization runs (sentinel-based) and that
/// neither the raw nor the sanitized query appears in logs above Debug.
/// </para>
/// <para>
/// <strong>ADR-018 kill switch (BINDING)</strong>: <see cref="BingGroundingOptions.Enabled"/>
/// is checked BEFORE any network call in each method. When disabled, the handler returns a
/// successful <see cref="ToolResult"/> whose <see cref="ToolResult.Summary"/> + payload text
/// carry a user-readable degradation message — no Bing call, no thread creation, no
/// semaphore acquisition.
/// </para>
/// <para>
/// <strong>Citations via Wave 7b metadata envelope</strong>: per
/// <see cref="ToolResultMetadataKeys.Citations"/>, the handler returns
/// <see cref="ToolResultCitation"/> envelopes with <c>SourceType = "BingGrounding"</c>.
/// The <c>ToolHandlerToAIFunctionAdapter</c> accumulates them into the per-chat-turn
/// <see cref="CitationContext"/> on chat-path invocations; on playbook-path invocations
/// citations flow through whichever accumulator a future playbook step wires in.
/// </para>
/// <para>
/// <strong>Dependencies</strong>: <see cref="AgentServiceClient"/>,
/// <see cref="IOptions{BingGroundingOptions}"/>, and an <see cref="ILogger{T}"/>. All
/// constructor-injected (auto-discovered via
/// <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c>). No
/// <see cref="QuerySanitizer"/> dependency — that class lives inside the legacy
/// <c>LegalResearchTools</c> as <c>internal static</c>; this handler also keeps a local
/// <c>internal static partial</c> sanitizer with identical regex set so the migration is
/// fully self-contained (the legacy class can be deleted by the main session once the
/// hardcoded factory block is removed).
/// </para>
/// <para>
/// <strong>Concurrency / rate-limit (ADR-016)</strong>: <see cref="AgentServiceClient"/>
/// already enforces a global semaphore + 30s acquire timeout — the handler does NOT add a
/// second gate (avoids double-counting). When the underlying client throws
/// <see cref="ConcurrencyLimitExceededException"/>, the handler converts to a
/// <see cref="ToolResult"/> with a user-readable degradation message rather than bubbling
/// the exception.
/// </para>
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Chat"/>. The
/// pre-R6 hardcoded LegalResearchTools registration was chat-only; playbook orchestration
/// for legal research is not exposed today (no <c>legal_research</c> node exists in the
/// 11 production node executors per NFR-08). Setting Chat-only preserves the existing
/// surface — playbook callers reading <c>sprk_availableincontexts</c> will not see this
/// handler.
/// </para>
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered; ZERO manual DI line.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>;
/// CRUD-side callers route through <c>PublicContracts</c> facades, never
/// directly into this handler.</item>
/// <item><strong>ADR-014</strong>: per-tenant safety — handler validates
/// <c>TenantId</c> on chat path. The <see cref="AgentServiceClient"/> thread cache is
/// tenant-keyed via its <c>BuildThreadCacheKey</c> helper.</item>
/// <item><strong>ADR-015</strong>: PII sanitization BEFORE every Bing call (binding);
/// telemetry emits handler name + outcome + IDs + method discriminator + query LENGTH
/// + result count + duration ONLY. NEVER the raw or sanitized query, NEVER result
/// bodies above Debug.</item>
/// <item><strong>ADR-016</strong>: rate-limit + concurrency gate live behind
/// <see cref="AgentServiceClient"/>; handler converts gate timeouts into user-readable
/// degradation messages.</item>
/// <item><strong>ADR-018</strong>: kill-switch checked on every method BEFORE any Bing
/// call; returns user-readable message + zero side effects when disabled.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler publish-size delta
/// ≤+0.1 MB.</item>
/// </list>
/// </remarks>
public partial class LegalResearchHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(LegalResearchHandler);

    internal const string MethodResearchLegal = "ResearchLegal";
    internal const string MethodLookupCase = "LookupCase";

    private static readonly HashSet<string> SupportedMethods = new(StringComparer.Ordinal)
    {
        MethodResearchLegal,
        MethodLookupCase
    };

    /// <summary>Thread key used for AgentServiceClient cache isolation of legal-research threads.</summary>
    private const string LegalResearchThreadKey = "legal-research-grounding";

    /// <summary>
    /// User-readable degradation message returned when the BingGrounding kill switch is off
    /// (ADR-018) in the ResearchLegal method path. Wording mirrors the pre-R6 legacy tool.
    /// </summary>
    internal const string ResearchLegalDisabledMessage =
        "Legal research via Bing Grounding is currently disabled. " +
        "Please consult your firm's internal legal research tools or contact your administrator.";

    /// <summary>
    /// User-readable degradation message returned when the BingGrounding kill switch is off
    /// (ADR-018) in the LookupCase method path. Wording mirrors the pre-R6 legacy tool.
    /// </summary>
    internal const string LookupCaseDisabledMessage =
        "Case citation lookup via Bing Grounding is currently disabled. " +
        "Please use your firm's legal research subscription (Westlaw, LexisNexis) directly.";

    /// <summary>
    /// User-readable degradation message returned when the AgentServiceClient concurrency
    /// gate (ADR-016) refuses the call. Wording mirrors the pre-R6 legacy tool.
    /// </summary>
    internal const string ResearchCapacityMessage =
        "Legal research is temporarily at capacity. Please try again in a few moments.";

    internal const string LookupCapacityMessage =
        "Case lookup is temporarily at capacity. Please try again in a few moments.";

    private readonly AgentServiceClient _agentServiceClient;
    private readonly BingGroundingOptions _options;
    private readonly ILogger<LegalResearchHandler> _logger;

    public LegalResearchHandler(
        AgentServiceClient agentServiceClient,
        IOptions<BingGroundingOptions> options,
        ILogger<LegalResearchHandler> logger)
    {
        _agentServiceClient = agentServiceClient ?? throw new ArgumentNullException(nameof(agentServiceClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Legal Research",
        Description: "Researches legal topics or looks up specific case citations via Azure AI Foundry " +
                     "Bing Grounding. Provides two methods: 'ResearchLegal' for broad topics, doctrines, " +
                     "statutes, and regulatory requirements; 'LookupCase' for a specific case citation. " +
                     "Both methods sanitize queries to strip client identifiers and PII before forwarding " +
                     "to Bing, and both register citations so the LLM can use [N] markers in its response.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition("topic", "Legal topic or question to research (ResearchLegal method); do not include client names or matter references.", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("citation", "Case citation in standard format (LookupCase method), e.g., '123 F.3d 456 (9th Cir. 2020)'.", ToolParameterType.String, Required: false)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    /// <remarks>
    /// Chat-only: matches the pre-R6 hardcoded registration. Setting
    /// <see cref="InvocationContextKind.Chat"/> means <c>ExecuteAsync</c> (the playbook
    /// path) is the default <c>NotSupportedException</c>-throwing inherited member; the
    /// data-driven block in <c>SprkChatAgentFactory</c> only exposes this handler to the
    /// LLM via chat function calling.
    /// </remarks>
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        // Playbook path is not supported (Chat-only handler) — Validate is still required
        // by IToolHandler; this implementation never fires in practice because the chat
        // factory does not register us for playbook contexts. Return Success to keep the
        // contract honest (a downstream caller invoking ExecuteAsync gets NotSupportedException).
        return ToolValidationResult.Success();
    }

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        if (string.IsNullOrWhiteSpace(context.ToolArgumentsJson))
            return ToolValidationResult.Failure("Tool arguments JSON is required for chat invocation.");

        var method = ResolveMethod(tool.Configuration);
        if (!SupportedMethods.Contains(method))
        {
            return ToolValidationResult.Failure(
                $"Configured method '{method}' is not supported. Use 'ResearchLegal' or 'LookupCase'.");
        }

        try
        {
            using var doc = JsonDocument.Parse(context.ToolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ToolValidationResult.Failure("Tool arguments must be a JSON object.");

            if (string.Equals(method, MethodResearchLegal, StringComparison.Ordinal))
            {
                if (!doc.RootElement.TryGetProperty("topic", out var topicProp) ||
                    topicProp.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(topicProp.GetString()))
                {
                    return ToolValidationResult.Failure(
                        "Tool arguments must include a non-empty 'topic' string field for the ResearchLegal method.");
                }
            }
            else
            {
                // LookupCase
                if (!doc.RootElement.TryGetProperty("citation", out var citProp) ||
                    citProp.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(citProp.GetString()))
                {
                    return ToolValidationResult.Failure(
                        "Tool arguments must include a non-empty 'citation' string field for the LookupCase method.");
                }
            }
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Failure($"Tool arguments JSON is malformed: {ex.Message}");
        }

        return ToolValidationResult.Success();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Playbook context is NOT supported — <see cref="SupportedInvocationContexts"/> is
    /// <see cref="InvocationContextKind.Chat"/>. The default interface member throws
    /// <see cref="NotSupportedException"/> which is the correct behavior here; this
    /// explicit implementation re-states the policy with a clearer message.
    /// </remarks>
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException(
            $"Handler '{HandlerId}' does not support playbook invocation. " +
            "LegalResearchHandler is chat-only (SupportedInvocationContexts = Chat).");
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;
        var method = ResolveMethod(tool.Configuration);
        var correlationLogId = $"session={context.ChatSessionId},decision={context.DecisionId}";

        try
        {
            _logger.LogInformation(
                "LegalResearchHandler chat-invocation method '{Method}' for session {ChatSessionId}, decision {DecisionId}",
                method, context.ChatSessionId, context.DecisionId);

            cancellationToken.ThrowIfCancellationRequested();

            var (topic, citation) = ParseChatArgs(context.ToolArgumentsJson);

            return string.Equals(method, MethodResearchLegal, StringComparison.Ordinal)
                ? await ExecuteResearchLegalAsync(
                    tool: tool,
                    rawTopic: topic,
                    startedAt: startedAt,
                    stopwatch: stopwatch,
                    correlationLogId: correlationLogId,
                    cancellationToken: cancellationToken)
                : await ExecuteLookupCaseAsync(
                    tool: tool,
                    rawCitation: citation,
                    startedAt: startedAt,
                    stopwatch: stopwatch,
                    correlationLogId: correlationLogId,
                    cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "LegalResearchHandler chat cancelled for session {ChatSessionId}, decision {DecisionId}, method '{Method}'",
                context.ChatSessionId, context.DecisionId, method);
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "LegalResearchHandler chat failed for session {ChatSessionId}, decision {DecisionId}, method '{Method}': {ErrorType}",
                context.ChatSessionId, context.DecisionId, method, ex.GetType().Name);
            return BuildInternalErrorResult(tool, ex, startedAt);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ResearchLegal — broad topic research
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> ExecuteResearchLegalAsync(
        AnalysisTool tool,
        string? rawTopic,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawTopic))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerIdValue, tool.Id, tool.Name,
                "topic is required for the ResearchLegal method.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        // ADR-015: Log only query LENGTH — never the query text itself.
        _logger.LogInformation(
            "LegalResearchHandler ({Correlation}) ResearchLegal start — queryLen={QueryLen}",
            correlationLogId, rawTopic!.Length);

        // ADR-018 kill switch — BEFORE any network call.
        if (!_options.Enabled)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "LegalResearchHandler ({Correlation}) ResearchLegal skipped — BingGrounding kill switch is disabled",
                correlationLogId);
            return BuildDegradationResult(tool, ResearchLegalDisabledMessage, startedAt);
        }

        // ADR-015: Sanitize BEFORE forwarding to Bing.
        var sanitizedTopic = QuerySanitizer.Sanitize(rawTopic!);

        return await RunResearchAndBuildResultAsync(
            tool: tool,
            sanitizedQuery: sanitizedTopic,
            operationName: "legal research",
            capacityMessage: ResearchCapacityMessage,
            methodLabel: MethodResearchLegal,
            startedAt: startedAt,
            stopwatch: stopwatch,
            correlationLogId: correlationLogId,
            cancellationToken: cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // LookupCase — specific citation lookup
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> ExecuteLookupCaseAsync(
        AnalysisTool tool,
        string? rawCitation,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawCitation))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerIdValue, tool.Id, tool.Name,
                "citation is required for the LookupCase method.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        // ADR-015: Log only citation LENGTH — never the citation text itself.
        _logger.LogInformation(
            "LegalResearchHandler ({Correlation}) LookupCase start — citationLen={CitationLen}",
            correlationLogId, rawCitation!.Length);

        // ADR-018 kill switch — BEFORE any network call.
        if (!_options.Enabled)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "LegalResearchHandler ({Correlation}) LookupCase skipped — BingGrounding kill switch is disabled",
                correlationLogId);
            return BuildDegradationResult(tool, LookupCaseDisabledMessage, startedAt);
        }

        // ADR-015: Sanitize the citation (strips PII prefixes; preserves citation tokens).
        var sanitizedCitation = QuerySanitizer.Sanitize(rawCitation!);

        // Anchor the Bing query to authoritative legal databases (mirrors legacy tool).
        var searchQuery = $"case law citation \"{sanitizedCitation}\" site:law.justia.com OR site:courtlistener.com OR site:scholar.google.com";

        return await RunResearchAndBuildResultAsync(
            tool: tool,
            sanitizedQuery: searchQuery,
            operationName: "case citation lookup",
            capacityMessage: LookupCapacityMessage,
            methodLabel: MethodLookupCase,
            startedAt: startedAt,
            stopwatch: stopwatch,
            correlationLogId: correlationLogId,
            cancellationToken: cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Shared Bing-grounding orchestration + result building
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> RunResearchAndBuildResultAsync(
        AnalysisTool tool,
        string sanitizedQuery,
        string operationName,
        string capacityMessage,
        string methodLabel,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        List<GroundingResult> results;
        try
        {
            results = await RunBingGroundingAsync(sanitizedQuery, cancellationToken).ConfigureAwait(false);
        }
        catch (FeatureDisabledException)
        {
            // AgentServiceClient itself was disabled (independent of BingGrounding kill switch).
            stopwatch.Stop();
            _logger.LogInformation(
                "LegalResearchHandler ({Correlation}) {Method} skipped — AgentServiceClient is disabled",
                correlationLogId, methodLabel);
            return BuildDegradationResult(tool, ResearchLegalDisabledMessage, startedAt);
        }
        catch (ConcurrencyLimitExceededException)
        {
            // ADR-016 gate timed out (held inside AgentServiceClient). Mirror the legacy
            // behavior: return user-readable degradation string rather than bubbling 429.
            stopwatch.Stop();
            _logger.LogWarning(
                "LegalResearchHandler ({Correlation}) {Method} concurrency limit reached — returning degradation",
                correlationLogId, methodLabel);
            return BuildDegradationResult(tool, capacityMessage, startedAt);
        }

        stopwatch.Stop();

        // ADR-015: log only result COUNT and timing — never the query or result bodies.
        _logger.LogInformation(
            "LegalResearchHandler ({Correlation}) {Method} ok resultCount={ResultCount} durationMs={Duration}",
            correlationLogId, methodLabel, results.Count, stopwatch.ElapsedMilliseconds);

        // Build citation envelopes for the Wave 7b adapter post-processing.
        var citations = results
            .Where(r => !string.IsNullOrWhiteSpace(r.Url))
            .Select(r => new ToolResultCitation(
                ChunkId: r.Url,
                SourceName: r.Title,
                PageNumber: null,
                Excerpt: TruncateSnippet(r.Snippet, CitationContext.MaxExcerptLength),
                SourceType: "BingGrounding",
                Url: r.Url,
                Snippet: TruncateSnippet(r.Snippet, CitationContext.MaxExcerptLength)))
            .ToArray();

        var formatted = FormatLegalResults(results, operationName);

        var metadata = new Dictionary<string, object?>
        {
            [ToolResultMetadataKeys.Citations] = citations
        };

        return ToolResult.Ok(
            HandlerIdValue, tool.Id, tool.Name,
            data: new LegalResearchPayload
            {
                Method = methodLabel,
                Content = formatted,
                ResultCount = results.Count
            },
            summary: $"Legal research ({operationName}) returned {results.Count} result(s).",
            confidence: results.Count == 0 ? 0.0 : 1.0,
            execution: new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                ModelCalls = 1
            }) with
        {
            Metadata = metadata
        };
    }

    /// <summary>
    /// Drives AgentServiceClient through the Bing Grounding agent: create/resume thread →
    /// send message → stream response → extract grounding annotations from the streamed text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Internal-virtual to permit subclass-override in unit tests (AgentServiceClient is
    /// sealed, so direct Moq is not possible). Production callers MUST NOT subclass this
    /// type; the seam exists exclusively for test injection of fake Bing results.
    /// </para>
    /// <para>
    /// ADR-015: this method is the single point where sanitizedQuery crosses the BFF
    /// boundary into Azure AI Foundry. Nothing about the query, response stream, or
    /// resulting annotations is logged here above Debug.
    /// </para>
    /// </remarks>
    internal virtual async Task<List<GroundingResult>> RunBingGroundingAsync(
        string sanitizedQuery,
        CancellationToken cancellationToken)
    {
        var threadId = await _agentServiceClient
            .CreateOrResumeThreadAsync(LegalResearchThreadKey, cancellationToken)
            .ConfigureAwait(false);

        await _agentServiceClient
            .SendMessageAsync(threadId, sanitizedQuery, cancellationToken)
            .ConfigureAwait(false);

        var sb = new StringBuilder();
        await foreach (var token in _agentServiceClient
                           .StreamResponseAsync(threadId, cancellationToken)
                           .ConfigureAwait(false))
        {
            sb.Append(token);
        }

        var responseText = sb.ToString();
        var groundingResults = ExtractGroundingAnnotations(responseText, _options.MaxResultsPerQuery);

        // ADR-015: count only — never content.
        _logger.LogDebug(
            "LegalResearchHandler BingGrounding extracted {AnnotationCount} annotations",
            groundingResults.Count);

        // If no annotations were parsed but the agent produced a text summary, expose it
        // as a single unlinked result so the LLM has the prose response (legacy parity).
        if (groundingResults.Count == 0 && responseText.Length > 0)
        {
            groundingResults.Add(new GroundingResult(
                Title: "Legal Research Summary",
                Url: string.Empty,
                Snippet: TruncateSnippet(responseText, CitationContext.MaxExcerptLength),
                Position: 1));
        }

        return groundingResults;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Argument parsing
    // ─────────────────────────────────────────────────────────────────────────────

    private static (string? Topic, string? Citation) ParseChatArgs(string? toolArgumentsJson)
    {
        if (string.IsNullOrWhiteSpace(toolArgumentsJson))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, null);

            var topic = doc.RootElement.TryGetProperty("topic", out var t)
                && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;

            var citation = doc.RootElement.TryGetProperty("citation", out var c)
                && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;

            return (topic, citation);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Resolves the <c>method</c> discriminator from the tool's configuration JSON. Defaults
    /// to <see cref="MethodResearchLegal"/> when missing (broader, less-targeted method).
    /// </summary>
    private static string ResolveMethod(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return MethodResearchLegal;

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return MethodResearchLegal;

            if (doc.RootElement.TryGetProperty("method", out var m)
                && m.ValueKind == JsonValueKind.String)
            {
                var v = m.GetString();
                if (!string.IsNullOrWhiteSpace(v))
                    return v!;
            }
        }
        catch (JsonException)
        {
            // Fall through to default.
        }

        return MethodResearchLegal;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Result formatting (preserved from legacy LegalResearchTools for LLM parity)
    // ─────────────────────────────────────────────────────────────────────────────

    private static string FormatLegalResults(List<GroundingResult> results, string operationName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Legal research ({operationName}) returned {results.Count} result(s).");
        sb.AppendLine();
        sb.AppendLine("**Note**: These results are from external public legal databases and should be " +
                      "verified against authoritative primary sources before relying on them in legal advice.");
        sb.AppendLine();

        foreach (var result in results)
        {
            var urlPart = string.IsNullOrWhiteSpace(result.Url) ? "" : $" — {result.Url}";
            sb.AppendLine($"[{result.Position}] [Legal Source] {result.Title}{urlPart}");
            if (!string.IsNullOrWhiteSpace(result.Snippet))
                sb.AppendLine($"    {result.Snippet}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Grounding annotation extraction (preserved from legacy LegalResearchTools)
    // ─────────────────────────────────────────────────────────────────────────────

    internal static List<GroundingResult> ExtractGroundingAnnotations(string responseText, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return new List<GroundingResult>();

        var results = new List<GroundingResult>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in ReferenceLinkPattern().Matches(responseText))
        {
            if (results.Count >= maxResults) break;

            var url = match.Groups["url"].Value;
            var title = match.Groups["title"].Value.Trim('"', '\'');

            if (!seenUrls.Add(url)) continue;

            results.Add(new GroundingResult(
                Title: string.IsNullOrWhiteSpace(title) ? ExtractDomainFromUrl(url) : title,
                Url: url,
                Snippet: string.Empty,
                Position: results.Count + 1));
        }

        foreach (Match match in InlineLinkPattern().Matches(responseText))
        {
            if (results.Count >= maxResults) break;

            var text = match.Groups["text"].Value;
            var url = match.Groups["url"].Value;

            if (!seenUrls.Add(url)) continue;

            if (int.TryParse(text.Trim(), out _)) continue;

            results.Add(new GroundingResult(
                Title: string.IsNullOrWhiteSpace(text) ? ExtractDomainFromUrl(url) : text,
                Url: url,
                Snippet: string.Empty,
                Position: results.Count + 1));
        }

        return results;
    }

    private static string ExtractDomainFromUrl(string url)
    {
        try
        {
            return new Uri(url).Host;
        }
        catch
        {
            return url.Length > 60 ? url[..60] + "..." : url;
        }
    }

    [GeneratedRegex(@"\[\d+\]:\s*(?<url>https?://[^\s""']+)(?:\s+""(?<title>[^""]+)"")?",
        RegexOptions.IgnoreCase | RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReferenceLinkPattern();

    [GeneratedRegex(@"\[(?<text>[^\]]+)\]\((?<url>https?://[^)]+)\)",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InlineLinkPattern();

    private static string TruncateSnippet(string text, int maxLength) =>
        text.Length > maxLength ? text[..maxLength] + "..." : text;

    // ─────────────────────────────────────────────────────────────────────────────
    // Result-builder helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a successful <see cref="ToolResult"/> carrying a user-readable degradation
    /// message in both <see cref="ToolResult.Summary"/> and the data payload. Used for both
    /// the kill-switch and concurrency-exhaustion paths so the LLM sees a graceful response
    /// rather than an error.
    /// </summary>
    private static ToolResult BuildDegradationResult(AnalysisTool tool, string message, DateTimeOffset startedAt) =>
        ToolResult.Ok(
            HandlerIdValue, tool.Id, tool.Name,
            data: new LegalResearchPayload
            {
                Method = "Disabled",
                Content = message,
                Message = message,
                ResultCount = 0
            },
            summary: message,
            confidence: 0.0,
            execution: new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                ModelCalls = 0
            });

    private static ToolResult BuildCancelledResult(AnalysisTool tool, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerIdValue, tool.Id, tool.Name,
            "Legal research was cancelled.",
            ToolErrorCodes.Cancelled,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    private static ToolResult BuildInternalErrorResult(AnalysisTool tool, Exception ex, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerIdValue, tool.Id, tool.Name,
            $"Legal research failed: {ex.Message}",
            ToolErrorCodes.InternalError,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    // ─────────────────────────────────────────────────────────────────────────────
    // Internal types
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. Carries the
    /// method discriminator + the formatted text content the chat agent renders.
    /// </summary>
    public sealed class LegalResearchPayload
    {
        [JsonPropertyName("method")]
        public string Method { get; set; } = MethodResearchLegal;

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("resultCount")]
        public int ResultCount { get; set; }
    }

    /// <summary>
    /// Internal record representing a single Bing Grounding result. Visible to the
    /// test project via <c>InternalsVisibleTo("Sprk.Bff.Api.Tests")</c> so tests can
    /// override <see cref="RunBingGroundingAsync"/> and return scripted results.
    /// </summary>
    internal sealed record GroundingResult(string Title, string Url, string Snippet, int Position);

    // ─────────────────────────────────────────────────────────────────────────────
    // Query sanitizer (ADR-015) — local copy of the legacy LegalResearchTools sanitizer
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Static helper that sanitizes legal queries before they leave the BFF boundary
    /// (ADR-015). Pattern set is identical to the legacy <c>LegalResearchTools.QuerySanitizer</c>
    /// (which the main session removes alongside the hardcoded factory block once this
    /// handler lands). Tests are intentionally written against THIS sanitizer so the
    /// migration is self-contained.
    /// </summary>
    /// <remarks>
    /// <para><strong>Stripped / replaced</strong>:</para>
    /// <list type="bullet">
    /// <item><c>"Client: &lt;name&gt;"</c> / <c>"client: &lt;name&gt;"</c> prefixes → removed entirely.</item>
    /// <item><c>"Matter NNNN-NNNN"</c> / <c>"Matter Ref: NNNN"</c> → replaced with <c>[MATTER-REF]</c>.</item>
    /// <item>Email addresses → replaced with <c>[EMAIL]</c>.</item>
    /// <item><c>"Re: Subject"</c> lines → removed.</item>
    /// </list>
    /// <para><strong>Preserved</strong>: case citations (e.g., "123 F.3d 456"), public legal entity
    /// names, statutory references, legal terms, jurisdiction names.</para>
    /// </remarks>
    internal static partial class QuerySanitizer
    {
        [GeneratedRegex(@"(?:^|(?<=[,;]\s*))Client(?:\s+Name)?:\s*[^,;:\n]+[,;]?\s*",
            RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
        private static partial Regex ClientPrefixPattern();

        [GeneratedRegex(@"\bMatter\s+(?:Ref(?:erence)?:?\s*)?\d[\d\-/]+\b",
            RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
        private static partial Regex MatterRefPattern();

        [GeneratedRegex(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
            RegexOptions.None, matchTimeoutMilliseconds: 500)]
        private static partial Regex EmailPattern();

        [GeneratedRegex(@"(?:^|\n)\s*Re:\s*[^\n]+\n?",
            RegexOptions.IgnoreCase | RegexOptions.Multiline, matchTimeoutMilliseconds: 500)]
        private static partial Regex ReSubjectPattern();

        [GeneratedRegex(@"\s{2,}", RegexOptions.None, matchTimeoutMilliseconds: 500)]
        private static partial Regex NormalizeWhitespacePattern();

        /// <summary>
        /// Sanitizes a legal query string by removing or replacing known PII patterns.
        /// Returns the sanitized string (may equal the input if no patterns matched).
        /// </summary>
        internal static string Sanitize(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return query;

            var sanitized = query;
            sanitized = ReSubjectPattern().Replace(sanitized, " ");
            sanitized = ClientPrefixPattern().Replace(sanitized, string.Empty);
            sanitized = MatterRefPattern().Replace(sanitized, "[MATTER-REF]");
            sanitized = EmailPattern().Replace(sanitized, "[EMAIL]");
            sanitized = NormalizeWhitespacePattern().Replace(sanitized.Trim(), " ");
            return sanitized;
        }
    }
}
