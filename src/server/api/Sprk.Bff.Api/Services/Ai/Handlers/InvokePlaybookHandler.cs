using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Generic chat-side typed handler that dispatches any tenant-accessible playbook by ID
/// via the <see cref="IInvokePlaybookAi"/> facade (R6 Pillar 3 / Q11 / task 021). Surfaced
/// to the LLM as the <c>invoke_playbook(playbookId, parameters)</c> function so a single
/// data-driven tool row can route to ANY registered playbook — replacing the specialized
/// <c>InvokeSummarizePlaybookTool</c> and <c>InvokeInsightsQueryTool</c> bridges (deleted
/// in Wave 10 / task 023).
/// </summary>
/// <remarks>
/// <para>
/// <strong>ADR-013 facade boundary</strong>: this handler injects ONLY
/// <see cref="IInvokePlaybookAi"/> (the public facade) — NEVER
/// <see cref="IPlaybookOrchestrationService"/>, <see cref="IPlaybookExecutionEngine"/>, or
/// any other AI-internal type. The chat-tool dispatch path is a CRUD-side caller of the
/// orchestration layer; the facade is the canonical bridge. See
/// <c>.claude/adr/ADR-013-ai-architecture.md</c> §"MUST NOT".
/// </para>
/// <para>
/// <strong>Tenant playbook visibility (FR-22 + NFR-14)</strong>: the handler validates
/// <c>playbookId</c> via <see cref="IPlaybookService.GetPlaybookAsync"/>. The BFF
/// authenticates against the tenant's Dataverse environment with a per-tenant credential,
/// so any playbook returned by the service is by definition tenant-accessible (Dataverse
/// rows are scoped per environment + per-row security). A null result indicates the
/// playbook does not exist OR is not visible to this tenant; both collapse to a uniform
/// <see cref="ToolErrorCodes.ValidationFailed"/> response (no information leakage about
/// which case it was). Per-tenant cache via <see cref="IMemoryCache"/> keyed by
/// <c>tenantId + playbookId</c> per ADR-014.
/// </para>
/// <para>
/// <strong>Parameter shape validation</strong>: the LLM-supplied <c>parameters</c> are
/// validated as a JSON object (per the tool's own JsonSchema — adapter-validated at
/// invocation boundary per task D-A-10). The PlaybookResponse's <c>ConfigJson</c> MAY
/// declare a per-playbook parameter schema; for task 021 we ship shape validation only
/// (object + dictionary projection) and document the per-playbook schema validation as a
/// future enhancement. The seed row's <c>sprk_jsonschema</c> uses
/// <c>parameters: { type: "object", additionalProperties: true }</c> so the adapter does
/// not pre-reject playbook-specific parameter keys.
/// </para>
/// <para>
/// <strong>Wave 7b envelope projection</strong>: the facade returns a
/// <see cref="PlaybookInvocationResult"/> that already carries aggregated
/// <see cref="ToolResultCitation"/> entries from terminal node outputs / RAG retrievals.
/// The handler forwards those into <see cref="ToolResult.Metadata"/> under
/// <see cref="ToolResultMetadataKeys.Citations"/> so the
/// <c>ToolHandlerToAIFunctionAdapter</c> accumulates them into the per-chat-turn
/// <see cref="Models.Ai.Chat.CitationContext"/>. No widget envelope is emitted from this
/// handler — sub-playbooks may emit their own widget events via their tool nodes; this
/// dispatcher's role is summary text + structured data + citations.
/// </para>
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Chat"/> only.
/// Playbook context invocation does not make sense for a generic playbook dispatcher
/// (playbooks invoke their own actions/tools — they don't chain to other playbooks via a
/// chat-tool indirection). Future R7+ work may extend this to
/// <see cref="InvocationContextKind.Both"/> if cross-playbook composition becomes a
/// requirement.
/// </para>
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered via
/// <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c>; ZERO manual DI line in
/// <c>AnalysisServicesModule</c>. ALL dependencies are already registered:
/// <see cref="IInvokePlaybookAi"/> (AddPublicContractsFacade), <see cref="IPlaybookService"/>
/// (AddPlaybookServices), <see cref="IMemoryCache"/> (CoreServiceCollectionExtensions),
/// <see cref="IHttpContextAccessor"/> (AddAnalysisOrchestrationServices).</item>
/// <item><strong>ADR-013</strong>: facade-only injection; lives under
/// <c>Services/Ai/Handlers/</c>; CRUD code never injects this type directly.</item>
/// <item><strong>ADR-014</strong>: playbook visibility lookups cached per
/// <c>tenantId + playbookId</c> for <see cref="VisibilityCacheTtl"/>; tenant prefix
/// prevents cross-tenant leakage.</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + playbookId + decision +
/// IDs + duration + citation-count ONLY. NEVER parameter values; NEVER user message text;
/// NEVER facade result text content above Debug level.</item>
/// <item><strong>ADR-018</strong>: NOT a feature flag. Capability gate intentionally
/// absent — this dispatcher is the generic mechanism available to ALL playbooks; per-
/// playbook authorization is enforced INSIDE the orchestration layer the facade wraps.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler publish-size
/// delta ≤+0.1 MB.</item>
/// </list>
/// </remarks>
public sealed class InvokePlaybookHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(InvokePlaybookHandler);

    /// <summary>
    /// ADR-014 per-tenant cache TTL for playbook-visibility lookups. Short enough that
    /// playbook visibility changes (e.g., a tenant admin un-sharing a playbook) propagate
    /// within minutes; long enough to amortize the Dataverse lookup across a chat turn.
    /// </summary>
    internal static readonly TimeSpan VisibilityCacheTtl = TimeSpan.FromMinutes(5);

    private const string CacheKeyPrefix = "invoke-playbook:visibility:";

    // DI-cycle break (2026-06-08): IInvokePlaybookAi is resolved lazily via IServiceProvider
    // to break the cycle introduced when this handler became an IToolHandler. The auto-
    // discovered IEnumerable<IToolHandler> backing IToolHandlerRegistry includes this
    // handler, and the facade transitively reaches back to IToolHandlerRegistry via
    // IPlaybookOrchestrationService → IAnalysisOrchestrationService → IToolHandlerRegistry.
    // Lazy resolution defers the dependency edge until first call, which is after the
    // container is fully built and no longer cycle-detecting.
    private readonly IServiceProvider _serviceProvider;
    private readonly IPlaybookService _playbookService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<InvokePlaybookHandler> _logger;

    public InvokePlaybookHandler(
        IServiceProvider serviceProvider,
        IPlaybookService playbookService,
        IHttpContextAccessor httpContextAccessor,
        IMemoryCache memoryCache,
        ILogger<InvokePlaybookHandler> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _playbookService = playbookService ?? throw new ArgumentNullException(nameof(playbookService));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private IInvokePlaybookAi InvokePlaybookAi =>
        _serviceProvider.GetRequiredService<IInvokePlaybookAi>();

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Invoke Playbook",
        Description: "Invoke any registered playbook by ID with parameters. Use this when the user " +
                     "requests an analysis already configured as a playbook (summarization, extraction, " +
                     "comparison, etc.). The playbook executes its full node graph and returns aggregated " +
                     "text, structured data, and citations.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "playbookId",
                "GUID of the playbook to invoke (sprk_analysisplaybook row ID). Required.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "parameters",
                "JSON object of caller-supplied parameters for template substitution in playbook node " +
                "prompts. Keys + values are deterministic identifiers / display values — never raw user " +
                "message text per ADR-015. May be omitted when the playbook has no parameters.",
                ToolParameterType.Object,
                Required: false)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    /// <remarks>
    /// Generic playbook dispatcher does NOT support playbook-context invocation (playbooks
    /// don't chain to other playbooks via chat-tool indirection). Returns a clear failure
    /// per the IToolHandler default — preserved here as an explicit guard.
    /// </remarks>
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "InvokePlaybookHandler is chat-context-only. Playbook-context invocation is unsupported.");

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        if (string.IsNullOrWhiteSpace(context.ToolArgumentsJson))
            return ToolValidationResult.Failure("Tool arguments JSON is required for chat invocation.");

        try
        {
            using var doc = JsonDocument.Parse(context.ToolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ToolValidationResult.Failure("Tool arguments must be a JSON object.");

            // playbookId — required, non-empty GUID string
            if (!doc.RootElement.TryGetProperty("playbookId", out var pidProp) ||
                pidProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(pidProp.GetString()))
            {
                return ToolValidationResult.Failure(
                    "Tool arguments must include a non-empty 'playbookId' string field.");
            }

            if (!Guid.TryParse(pidProp.GetString(), out _))
            {
                return ToolValidationResult.Failure(
                    "'playbookId' must be a valid GUID.");
            }

            // parameters — optional; when present MUST be a JSON object (not array / scalar)
            if (doc.RootElement.TryGetProperty("parameters", out var paramsProp) &&
                paramsProp.ValueKind is not JsonValueKind.Object and not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                return ToolValidationResult.Failure(
                    "'parameters' must be a JSON object when provided.");
            }
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Failure($"Tool arguments JSON is malformed: {ex.Message}");
        }

        return ToolValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "InvokePlaybookHandler is chat-context-only. Playbook-context invocation is unsupported.",
            ToolErrorCodes.ValidationFailed,
            new ToolExecutionMetadata { StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow }));

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;
        var correlationLogId = $"session={context.ChatSessionId},decision={context.DecisionId}";

        // Parse args — adapter has already schema-validated, but we re-project defensively.
        if (!TryParseArgs(context.ToolArgumentsJson, out var playbookId, out var parameters, out var parseError))
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "InvokePlaybookHandler ({Correlation}) argument parse failed: {Error}",
                correlationLogId, parseError);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                parseError ?? "Tool arguments could not be parsed.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        // ADR-015: log handler + IDs + parameterCount; NEVER parameter VALUES.
        _logger.LogInformation(
            "InvokePlaybookHandler ({Correlation}) dispatch start playbookId={PlaybookId} tenantId={TenantId} parameterCount={ParameterCount}",
            correlationLogId, playbookId, context.TenantId, parameters?.Count ?? 0);

        // Tenant visibility check via the cached lookup. ADR-014: per-tenant cache prefix.
        bool visible;
        try
        {
            visible = await IsTenantVisibleAsync(context.TenantId, playbookId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "InvokePlaybookHandler ({Correlation}) visibility lookup failed: {ErrorType}",
                correlationLogId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Playbook visibility lookup failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        if (!visible)
        {
            stopwatch.Stop();
            // ADR-015: log decision; uniform message irrespective of "not-found vs no-access" so
            // the LLM (and downstream observability) can't infer cross-tenant existence.
            _logger.LogInformation(
                "InvokePlaybookHandler ({Correlation}) visibility denied playbookId={PlaybookId} tenantId={TenantId} in {Duration}ms",
                correlationLogId, playbookId, context.TenantId, stopwatch.ElapsedMilliseconds);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Playbook '{playbookId}' is not available in this tenant or does not exist.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        // Resolve HttpContext for OBO auth required by the facade. When absent (e.g.,
        // background invocation), surface a clean failure rather than passing null and
        // tripping the facade's ArgumentNullException.
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "InvokePlaybookHandler ({Correlation}) HttpContext unavailable — handler requires a request scope for OBO authentication",
                correlationLogId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Playbook invocation requires an active HTTP request scope.",
                ToolErrorCodes.DependencyUnavailable,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        var invocationContext = new PlaybookInvocationContext
        {
            TenantId = context.TenantId,
            HttpContext = httpContext,
            CorrelationId = Activity.Current?.Id ?? context.DecisionId.ToString("N")
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await InvokePlaybookAi
                .InvokePlaybookAsync(playbookId, parameters, invocationContext, cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();

            // ADR-015: emit decision + counts + duration only (NOT text content).
            _logger.LogInformation(
                "InvokePlaybookHandler ({Correlation}) dispatch complete playbookId={PlaybookId} runId={RunId} success={Success} citationCount={CitationCount} in {Duration}ms",
                correlationLogId, playbookId, result.RunId, result.Success, result.Citations.Count, stopwatch.ElapsedMilliseconds);

            return ProjectFacadeResult(result, tool, startedAt);
        }
        catch (OperationCanceledException)
        {
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Sprk.Bff.Api.Configuration.FeatureDisabledException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex,
                "InvokePlaybookHandler ({Correlation}) facade reports feature disabled: {Reason}",
                correlationLogId, ex.Message);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Playbook invocation is currently unavailable (feature disabled).",
                ToolErrorCodes.DependencyUnavailable,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "InvokePlaybookHandler ({Correlation}) dispatch failed playbookId={PlaybookId}: {ErrorType}",
                correlationLogId, playbookId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Playbook invocation failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Tenant visibility
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<bool> IsTenantVisibleAsync(
        string tenantId,
        Guid playbookId,
        CancellationToken cancellationToken)
    {
        // ADR-014: cache key MUST include tenantId prefix to prevent cross-tenant leakage.
        var cacheKey = $"{CacheKeyPrefix}{tenantId}:{playbookId:D}";

        if (_memoryCache.TryGetValue<bool>(cacheKey, out var cachedVisible))
        {
            return cachedVisible;
        }

        // Cache miss — query Dataverse. The BFF authenticates per-tenant against the
        // Dataverse environment; a non-null response means the row exists and is reachable
        // under the tenant's auth context. We collapse "not found" + "no access" + "soft-
        // deleted" into the same uniform false-result to avoid information leakage about
        // cross-tenant playbook existence.
        PlaybookResponse? response = null;
        try
        {
            response = await _playbookService.GetPlaybookAsync(playbookId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Transient lookup failure — DO NOT cache; surface to caller so the handler
            // returns InternalError rather than silently denying the playbook.
            _logger.LogWarning(ex,
                "InvokePlaybookHandler visibility lookup transient failure for playbookId={PlaybookId} tenantId={TenantId} — not caching",
                playbookId, tenantId);
            throw;
        }

        var visible = response is not null && response.IsActive;

        // Cache both positive and negative results to absorb LLM retry storms (the LLM may
        // try the same invalid id multiple times within a turn). Negative cache TTL matches
        // positive — a 5-minute window is acceptable because the LLM cannot influence
        // tenant-admin visibility changes.
        _memoryCache.Set(cacheKey, visible, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = VisibilityCacheTtl,
            Size = 1
        });

        return visible;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Argument parsing
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse <c>playbookId</c> + <c>parameters</c> from the chat tool-call arguments JSON.
    /// Returns false + an error string when the JSON is malformed or required fields are
    /// missing — ValidateChat catches most of these but we re-project defensively for the
    /// dispatch path so test fixtures that skip ValidateChat still get clear diagnostics.
    /// </summary>
    private static bool TryParseArgs(
        string? toolArgumentsJson,
        out Guid playbookId,
        out IReadOnlyDictionary<string, string>? parameters,
        out string? error)
    {
        playbookId = Guid.Empty;
        parameters = null;
        error = null;

        if (string.IsNullOrWhiteSpace(toolArgumentsJson))
        {
            error = "Tool arguments JSON is required.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Tool arguments must be a JSON object.";
                return false;
            }

            if (!doc.RootElement.TryGetProperty("playbookId", out var pidProp) ||
                pidProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(pidProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'playbookId' string field.";
                return false;
            }

            if (!Guid.TryParse(pidProp.GetString(), out playbookId))
            {
                error = "'playbookId' must be a valid GUID.";
                return false;
            }

            if (doc.RootElement.TryGetProperty("parameters", out var paramsProp) &&
                paramsProp.ValueKind == JsonValueKind.Object)
            {
                var dict = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var prop in paramsProp.EnumerateObject())
                {
                    // Per facade contract, parameters are <string,string> for template
                    // substitution. Scalar non-string values are coerced to their JSON
                    // string form so the LLM can pass numbers/booleans without rejection.
                    dict[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => string.Empty,
                        _ => prop.Value.GetRawText()
                    };
                }
                parameters = dict;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Tool arguments JSON is malformed: {ex.Message}";
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Result projection (Wave 7b envelope)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Project the facade's <see cref="PlaybookInvocationResult"/> into a
    /// <see cref="ToolResult"/>, preserving the Wave 7b citations-in-metadata envelope so
    /// the adapter forwards citations into the per-chat-turn CitationContext.
    /// </summary>
    private ToolResult ProjectFacadeResult(
        PlaybookInvocationResult result,
        AnalysisTool tool,
        DateTimeOffset startedAt)
    {
        var execMetadata = new ToolExecutionMetadata
        {
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            ModelCalls = 0 // Aggregate model calls happen inside the playbook; not surfaced here.
        };

        if (!result.Success)
        {
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                result.ErrorMessage ?? "Playbook invocation failed.",
                result.ErrorCode ?? ToolErrorCodes.InternalError,
                execMetadata);
        }

        var summary = result.TextContent ?? $"Playbook completed (runId={result.RunId:D}).";

        var payload = new InvokePlaybookPayload
        {
            RunId = result.RunId,
            TextContent = result.TextContent,
            StructuredData = result.StructuredData,
            CitationCount = result.Citations.Count,
            DurationMs = (long)result.Duration.TotalMilliseconds
        };

        var ok = ToolResult.Ok(
            HandlerId,
            tool.Id,
            tool.Name,
            data: payload,
            summary: summary,
            confidence: result.Confidence,
            execution: execMetadata);

        // Wave 7b: forward citations under the well-known metadata key so the adapter
        // accumulates them into the per-chat-turn CitationContext. Skip when empty —
        // adapter no-ops on empty metadata, but the explicit skip keeps the assertion
        // pattern in tests easier to read.
        if (result.Citations.Count > 0)
        {
            ok = ok with
            {
                Metadata = new Dictionary<string, object?>
                {
                    [ToolResultMetadataKeys.Citations] = result.Citations
                }
            };
        }

        return ok;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Error-result builders
    // ─────────────────────────────────────────────────────────────────────────────

    private ToolResult BuildCancelledResult(AnalysisTool tool, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "Playbook invocation was cancelled.",
            ToolErrorCodes.Cancelled,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    // ─────────────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. Carries the
    /// run identifier + aggregated text + structured data + citation count + duration —
    /// suitable for the LLM to render or summarize in its next assistant turn. The full
    /// citation envelopes are forwarded via <see cref="ToolResult.Metadata"/> under
    /// <see cref="ToolResultMetadataKeys.Citations"/> (Wave 7b adapter contract).
    /// </summary>
    public sealed class InvokePlaybookPayload
    {
        /// <summary>Deterministic run identifier assigned by the orchestration layer.</summary>
        [JsonPropertyName("runId")]
        public Guid RunId { get; set; }

        /// <summary>Aggregated text content from terminal output nodes. Null when no text emitted.</summary>
        [JsonPropertyName("textContent")]
        public string? TextContent { get; set; }

        /// <summary>Aggregated structured data from terminal output nodes. Null when no data emitted.</summary>
        [JsonPropertyName("structuredData")]
        public JsonElement? StructuredData { get; set; }

        /// <summary>Count of citation envelopes accumulated across the run.</summary>
        [JsonPropertyName("citationCount")]
        public int CitationCount { get; set; }

        /// <summary>Total wall-clock duration of the run in milliseconds.</summary>
        [JsonPropertyName("durationMs")]
        public long DurationMs { get; set; }
    }
}
