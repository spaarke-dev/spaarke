using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Ai.Foundry;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat- and playbook-invocable typed handler that runs caller-supplied data excerpts
/// through the Azure AI Foundry Code Interpreter sandbox (R6 Wave 8). Replaces the legacy
/// hardcoded <c>CodeInterpreterTools</c> class previously instantiated in
/// <c>SprkChatAgentFactory.ResolveTools</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One handler, two methods</strong>: two <c>sprk_analysistool</c> rows
/// (<c>CODE-ANALYZE</c> and <c>CODE-CHART</c>) share this single
/// <c>sprk_handlerclass = CodeInterpreterHandler</c>. Each row's
/// <c>sprk_configuration.method</c> selects the internal method. This mirrors the
/// <see cref="KnowledgeRetrievalHandler"/> + <see cref="TextRefinementHandler"/> shape
/// (R6 Wave 7 Q9) — distinct LLM tools with distinct descriptions + parameter shapes, one
/// handler class.
/// </para>
/// <list type="bullet">
/// <item><c>method = "AnalyzeData"</c> — analyze tabular/CSV data to answer an analysis
/// question via the Code Interpreter sandbox. Registers a citation envelope so the frontend
/// can attribute the result. No widget event is emitted (the analysis is a plain text answer).</item>
/// <item><c>method = "GenerateChart"</c> — generate a chart image (bar/line/pie) via matplotlib
/// in the sandbox. Registers a citation envelope and emits an <c>output_pane</c>
/// <c>ChartViewer</c> widget envelope carrying the base-64 chart image so the frontend can
/// render the chart in the output pane. The chat-visible text still embeds the chart inline
/// as a markdown image data URI (preserves the pre-Wave-8 user-visible behavior).</item>
/// </list>
/// <para>
/// <strong>Capability gate (R6 Wave 7b infrastructure)</strong>: the corresponding
/// <c>sprk_analysistool</c> rows set <c>sprk_requiredcapability = "code_interpreter"</c>
/// (= <see cref="Sprk.Bff.Api.Models.Ai.Chat.PlaybookCapabilities.CodeInterpreter"/>). The
/// data-driven block in <c>SprkChatAgentFactory.ResolveTools</c> applies
/// <c>IsCapabilityGateSatisfied</c> at session start and silently withholds these rows when
/// the playbook's capability set lacks the value. Standalone chat (no playbook, capabilities
/// = <c>CoreCapabilities</c>) does NOT include <c>code_interpreter</c>, so this handler is
/// unreachable from standalone chat — preserving the pre-Wave-8 security boundary enforced
/// by the hardcoded <c>if (capabilities.Contains(PlaybookCapabilities.CodeInterpreter))</c>
/// block that this migration removes.
/// </para>
/// <para>
/// <strong>Citations + widget events</strong>: per the R6 Wave 7b infrastructure contract,
/// the handler returns citation envelopes + (for GenerateChart) a widget envelope via
/// <see cref="ToolResult.Metadata"/> using <see cref="ToolResultMetadataKeys.Citations"/> +
/// <see cref="ToolResultMetadataKeys.Widget"/>. The <c>ToolHandlerToAIFunctionAdapter</c>
/// accumulates citations into the per-chat-turn <c>CitationContext</c> and emits the SSE
/// event via the captured writer delegate.
/// </para>
/// <para>
/// <strong>Dependencies</strong>: <see cref="CodeInterpreterBridge"/> +
/// <see cref="IOptions{TOptions}"/> of <see cref="CodeInterpreterOptions"/>. Resolved via
/// constructor injection (auto-discovered by
/// <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c>).
/// </para>
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Chat"/>. The legacy
/// <c>CodeInterpreterTools</c> was registered only for chat; playbook nodes do not invoke
/// the sandbox today (the playbook path runs through the 11 production node executors per
/// NFR-08, none of which are Code Interpreter). Set to <c>Chat</c> only so the playbook
/// dispatch path does not accidentally expose the sandbox to playbook orchestration.
/// </para>
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered; ZERO manual DI line. The handler's
/// dependencies (<see cref="CodeInterpreterBridge"/> + <see cref="CodeInterpreterOptions"/>)
/// are already registered for the legacy <c>CodeInterpreterTools</c>; no new DI registrations
/// are required.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; CRUD-side code
/// never injects this handler.</item>
/// <item><strong>ADR-014</strong>: per-tenant safety — handler validates <c>TenantId</c> on
/// both chat and playbook paths. The sandbox itself is tenant-agnostic (each invocation
/// creates an ephemeral Foundry thread that is invalidated post-call), so no cross-tenant
/// leakage path exists; the tenant check exists for log correlation.</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + outcome + IDs + duration +
/// length buckets ONLY. NEVER the data excerpt, the analysis question, the chart series, or
/// the sandbox output. Only caller-supplied data excerpts are forwarded to the sandbox; the
/// handler does NOT auto-fetch any external resources.</item>
/// <item><strong>ADR-016</strong>: rate-limiting is enforced inside this handler via a static
/// <see cref="SemaphoreSlim"/> bounded by <see cref="CodeInterpreterOptions.MaxConcurrency"/>
/// (preserves the pre-Wave-8 behavior — the legacy <c>CodeInterpreterTools</c> owned this
/// gate and the bridge does not re-check). The gate is initialised lazily on first
/// construction and re-used across all handler instances (handlers are scoped per request).</item>
/// <item><strong>ADR-018</strong>: kill switch — <see cref="CodeInterpreterOptions.Enabled"/>
/// is checked before EVERY sandbox call. When disabled, the handler returns a successful
/// <see cref="ToolResult"/> whose <c>Summary</c> + <c>data.message</c> carry a user-readable
/// unavailability string (no exception). This preserves the pre-Wave-8 contract where the
/// AI model can gracefully inform the user the feature is off.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler publish-size delta
/// ≤+0.1 MB.</item>
/// </list>
/// </remarks>
public class CodeInterpreterHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(CodeInterpreterHandler);

    internal const string MethodAnalyzeData = "AnalyzeData";
    internal const string MethodGenerateChart = "GenerateChart";

    private static readonly HashSet<string> SupportedMethods = new(StringComparer.Ordinal)
    {
        MethodAnalyzeData,
        MethodGenerateChart
    };

    private static readonly HashSet<string> SupportedChartTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bar", "line", "pie"
    };

    /// <summary>
    /// Static concurrency gate (ADR-016) — bounds sandbox calls across all
    /// <see cref="CodeInterpreterHandler"/> instances. Mirrors the legacy
    /// <c>CodeInterpreterTools.s_concurrencyGate</c> design: the gate is shared
    /// across all handler instances in the same BFF process so the limit holds
    /// across concurrent chat sessions.
    /// </summary>
    private static SemaphoreSlim? s_concurrencyGate;
    private static readonly object s_gateLock = new();

    private readonly CodeInterpreterBridge _bridge;
    private readonly CodeInterpreterOptions _options;
    private readonly ILogger<CodeInterpreterHandler> _logger;

    public CodeInterpreterHandler(
        CodeInterpreterBridge bridge,
        IOptions<CodeInterpreterOptions> options,
        ILogger<CodeInterpreterHandler> logger)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // ADR-016: lazily initialise the static gate on first construction.
        // Subsequent constructions skip re-init — the gate is shared across handler instances.
        lock (s_gateLock)
        {
            s_concurrencyGate ??= new SemaphoreSlim(
                initialCount: _options.MaxConcurrency,
                maxCount: _options.MaxConcurrency);
        }
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Code Interpreter",
        Description: "Runs caller-supplied data excerpts through a Python sandbox (Azure AI Foundry " +
                     "Code Interpreter). Provides two methods: 'AnalyzeData' answers a question about " +
                     "tabular/CSV data by computing statistics, trends, or comparisons; 'GenerateChart' " +
                     "produces a bar / line / pie chart image from a JSON data series. Only caller-supplied " +
                     "data excerpts are sent to the sandbox — never full documents or PII.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain", "application/json" },
        Parameters: new[]
        {
            new ToolParameterDefinition("data", "CSV or tabular data excerpt to analyze (AnalyzeData method).", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("question", "Specific analysis question to answer using the data (AnalyzeData method).", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("dataSeries", "Data series as JSON array (e.g., [{\"label\":\"Q1\",\"value\":120}]) (GenerateChart method).", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("chartType", "Chart type: bar, line, or pie (GenerateChart method).", ToolParameterType.String, Required: false)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    /// <remarks>
    /// Chat-only. The legacy <c>CodeInterpreterTools</c> was registered exclusively for chat; the
    /// 11 production node executors (NFR-08) do not include a Code Interpreter executor, so
    /// playbook orchestration has no call path into this handler today.
    /// </remarks>
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

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
                $"Configured method '{method}' is not supported. Use 'AnalyzeData' or 'GenerateChart'.");
        }

        try
        {
            using var doc = JsonDocument.Parse(context.ToolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ToolValidationResult.Failure("Tool arguments must be a JSON object.");

            if (string.Equals(method, MethodAnalyzeData, StringComparison.Ordinal))
            {
                if (!HasNonEmptyString(doc.RootElement, "data"))
                    return ToolValidationResult.Failure(
                        "Tool arguments must include a non-empty 'data' string field for the AnalyzeData method.");

                if (!HasNonEmptyString(doc.RootElement, "question"))
                    return ToolValidationResult.Failure(
                        "Tool arguments must include a non-empty 'question' string field for the AnalyzeData method.");
            }
            else
            {
                if (!HasNonEmptyString(doc.RootElement, "dataSeries"))
                    return ToolValidationResult.Failure(
                        "Tool arguments must include a non-empty 'dataSeries' string field for the GenerateChart method.");

                if (!HasNonEmptyString(doc.RootElement, "chartType"))
                    return ToolValidationResult.Failure(
                        "Tool arguments must include a non-empty 'chartType' string field for the GenerateChart method.");
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
    /// Playbook-context path. Not used today — <see cref="SupportedInvocationContexts"/> is
    /// <see cref="InvocationContextKind.Chat"/>, so the playbook dispatcher will not route here.
    /// Provided as a defensive guard: if the contract were extended in the future, the playbook
    /// path returns an error result rather than throwing.
    /// </remarks>
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "CodeInterpreterHandler does not support playbook invocation. Use chat invocation only.",
            ToolErrorCodes.ValidationFailed,
            new ToolExecutionMetadata { StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow }));
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
                "CodeInterpreterHandler chat-invocation method '{Method}' for session {ChatSessionId}, decision {DecisionId}",
                method, context.ChatSessionId, context.DecisionId);

            // ADR-018: Kill switch — return a successful ToolResult carrying a user-readable
            // unavailability message so the AI model can gracefully inform the user. No bridge
            // call. No exception. Matches the legacy CodeInterpreterTools behavior.
            if (!_options.Enabled)
            {
                _logger.LogDebug(
                    "CodeInterpreterHandler ({Correlation}) method '{Method}' skipped — feature disabled via kill switch",
                    correlationLogId, method);

                stopwatch.Stop();
                return BuildKillSwitchResult(tool, method, startedAt);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var args = ParseChatArgs(context.ToolArgumentsJson);

            return await DispatchAsync(
                method: method,
                args: args,
                tool: tool,
                startedAt: startedAt,
                stopwatch: stopwatch,
                correlationLogId: correlationLogId,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "CodeInterpreterHandler chat cancelled for session {ChatSessionId}, decision {DecisionId}, method '{Method}'",
                context.ChatSessionId, context.DecisionId, method);
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CodeInterpreterHandler chat failed for session {ChatSessionId}, decision {DecisionId}, method '{Method}': {ErrorType}",
                context.ChatSessionId, context.DecisionId, method, ex.GetType().Name);
            return BuildInternalErrorResult(tool, ex, startedAt);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Dispatcher
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> DispatchAsync(
        string method,
        CodeInterpreterArgs args,
        AnalysisTool tool,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(method, MethodAnalyzeData, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(args.Data) || string.IsNullOrWhiteSpace(args.Question))
            {
                stopwatch.Stop();
                return ToolResult.Error(
                    HandlerId, tool.Id, tool.Name,
                    "data and question are required for the AnalyzeData method.",
                    ToolErrorCodes.ValidationFailed,
                    new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
            }

            return await ExecuteAnalyzeDataAsync(
                args.Data!, args.Question!, tool, startedAt, stopwatch, correlationLogId, cancellationToken);
        }

        // GenerateChart
        if (string.IsNullOrWhiteSpace(args.DataSeries) || string.IsNullOrWhiteSpace(args.ChartType))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "dataSeries and chartType are required for the GenerateChart method.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        return await ExecuteGenerateChartAsync(
            args.DataSeries!, args.ChartType!, tool, startedAt, stopwatch, correlationLogId, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // AnalyzeData
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> ExecuteAnalyzeDataAsync(
        string data,
        string question,
        AnalysisTool tool,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        // ADR-015: log lengths only — NEVER content.
        _logger.LogInformation(
            "CodeInterpreterHandler ({Correlation}) AnalyzeData starting — dataLen={DataLen}, questionLen={QuestionLen}",
            correlationLogId, data.Length, question.Length);

        // ADR-016: acquire concurrency semaphore.
        var timeout = TimeSpan.FromSeconds(_options.SandboxTimeoutSeconds);
        if (!await s_concurrencyGate!.WaitAsync(timeout, cancellationToken))
        {
            _logger.LogWarning(
                "CodeInterpreterHandler ({Correlation}) AnalyzeData rejected — concurrency limit reached (max {MaxConcurrency})",
                correlationLogId, _options.MaxConcurrency);

            stopwatch.Stop();
            return BuildBusyResult(tool, MethodAnalyzeData, startedAt);
        }

        try
        {
            var prompt = BuildAnalysisPrompt(data, question);
            var result = await InvokeBridgeAsync(prompt, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            _logger.LogInformation(
                "CodeInterpreterHandler ({Correlation}) AnalyzeData completed — outputLen={OutputLen} in {Duration}ms",
                correlationLogId, result.Output.Length, stopwatch.ElapsedMilliseconds);

            var formatted = FormatAnalysisResult(question, result);
            var citation = BuildAnalysisCitation(question, result.Output);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new CodeInterpreterPayload
                {
                    Method = MethodAnalyzeData,
                    Content = formatted,
                    OutputLength = result.Output.Length,
                    HasChart = false
                },
                summary: $"Code Interpreter completed data analysis for question of {question.Length} chars.",
                confidence: 1.0,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ModelCalls = 1
                }) with
            {
                Metadata = new Dictionary<string, object?>
                {
                    [ToolResultMetadataKeys.Citations] = new[] { citation }
                }
            };
        }
        catch (FeatureDisabledException ex)
        {
            // ADR-018: bridge-level kill switch (can race the handler-side check if config changes mid-call).
            _logger.LogWarning(ex,
                "CodeInterpreterHandler ({Correlation}) AnalyzeData: feature disabled mid-call",
                correlationLogId);

            stopwatch.Stop();
            return BuildKillSwitchResult(tool, MethodAnalyzeData, startedAt);
        }
        catch (ConcurrencyLimitExceededException ex)
        {
            // ADR-016: bridge-level concurrency rejection (can race the handler-side gate).
            _logger.LogWarning(ex,
                "CodeInterpreterHandler ({Correlation}) AnalyzeData: bridge concurrency exceeded",
                correlationLogId);

            stopwatch.Stop();
            return BuildBusyResult(tool, MethodAnalyzeData, startedAt);
        }
        finally
        {
            s_concurrencyGate!.Release();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // GenerateChart
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> ExecuteGenerateChartAsync(
        string dataSeries,
        string chartType,
        AnalysisTool tool,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        // Normalise + validate chart type — fall back to "bar" on unsupported value
        // (preserves legacy CodeInterpreterTools forgiveness).
        var normalizedChartType = chartType.Trim().ToLowerInvariant();
        if (!SupportedChartTypes.Contains(normalizedChartType))
        {
            _logger.LogWarning(
                "CodeInterpreterHandler ({Correlation}) GenerateChart: unsupported chartType={ChartType}; defaulting to bar",
                correlationLogId, chartType);
            normalizedChartType = "bar";
        }

        // ADR-015: log lengths only.
        _logger.LogInformation(
            "CodeInterpreterHandler ({Correlation}) GenerateChart starting — dataSeriesLen={DataSeriesLen}, chartType={ChartType}",
            correlationLogId, dataSeries.Length, normalizedChartType);

        // ADR-016: acquire concurrency semaphore.
        var timeout = TimeSpan.FromSeconds(_options.SandboxTimeoutSeconds);
        if (!await s_concurrencyGate!.WaitAsync(timeout, cancellationToken))
        {
            _logger.LogWarning(
                "CodeInterpreterHandler ({Correlation}) GenerateChart rejected — concurrency limit reached (max {MaxConcurrency})",
                correlationLogId, _options.MaxConcurrency);

            stopwatch.Stop();
            return BuildBusyResult(tool, MethodGenerateChart, startedAt);
        }

        try
        {
            var prompt = BuildChartPrompt(dataSeries, normalizedChartType);
            var result = await InvokeBridgeAsync(prompt, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            var hasChart = !string.IsNullOrWhiteSpace(result.ChartBase64);

            _logger.LogInformation(
                "CodeInterpreterHandler ({Correlation}) GenerateChart completed — chartType={ChartType}, hasChart={HasChart}, outputLen={OutputLen} in {Duration}ms",
                correlationLogId, normalizedChartType, hasChart, result.Output.Length, stopwatch.ElapsedMilliseconds);

            var formatted = FormatChartResult(normalizedChartType, result);
            var citation = BuildChartCitation(normalizedChartType, result.Output);

            var metadata = new Dictionary<string, object?>
            {
                [ToolResultMetadataKeys.Citations] = new[] { citation }
            };

            // Emit an output_pane ChartViewer widget envelope so frontends can render the chart in
            // the output pane. The chat-visible text still embeds the chart inline as a markdown
            // image data URI (FormatChartResult preserves the pre-Wave-8 behavior).
            // ADR-015: widget data carries display metadata (chart type, output length, hasChart)
            // + the base-64 chart payload. The chart payload is AI-generated sandbox output (not
            // user content). The description text excerpt is bounded by the citation envelope's
            // length cap; we forward the full base-64 image so the frontend can render it.
            metadata[ToolResultMetadataKeys.Widget] = new ToolResultWidget(
                PaneType: "output_pane",
                WidgetType: "ChartViewer",
                Data: new
                {
                    chartType = normalizedChartType,
                    hasChart,
                    chartBase64 = result.ChartBase64,
                    outputLength = result.Output.Length
                });

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new CodeInterpreterPayload
                {
                    Method = MethodGenerateChart,
                    Content = formatted,
                    OutputLength = result.Output.Length,
                    HasChart = hasChart,
                    ChartType = normalizedChartType
                },
                summary: $"Code Interpreter generated a {normalizedChartType} chart " +
                         (hasChart ? "with inline image data." : "(no inline image)."),
                confidence: 1.0,
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
        catch (FeatureDisabledException ex)
        {
            _logger.LogWarning(ex,
                "CodeInterpreterHandler ({Correlation}) GenerateChart: feature disabled mid-call",
                correlationLogId);

            stopwatch.Stop();
            return BuildKillSwitchResult(tool, MethodGenerateChart, startedAt);
        }
        catch (ConcurrencyLimitExceededException ex)
        {
            _logger.LogWarning(ex,
                "CodeInterpreterHandler ({Correlation}) GenerateChart: bridge concurrency exceeded",
                correlationLogId);

            stopwatch.Stop();
            return BuildBusyResult(tool, MethodGenerateChart, startedAt);
        }
        finally
        {
            s_concurrencyGate!.Release();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Prompt builders (preserved verbatim from legacy CodeInterpreterTools)
    // ─────────────────────────────────────────────────────────────────────────────

    private static string BuildAnalysisPrompt(string data, string question)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a data analysis assistant. Analyze the following data to answer the question.");
        sb.AppendLine("Use Python to compute statistics, identify trends, or derive insights as needed.");
        sb.AppendLine("Return a clear, concise answer in plain text. Do NOT include code blocks in your response.");
        sb.AppendLine();
        sb.AppendLine("DATA:");
        sb.AppendLine(data);
        sb.AppendLine();
        sb.AppendLine("QUESTION:");
        sb.AppendLine(question);
        sb.AppendLine();
        sb.AppendLine("Provide your answer with key findings. If the data is insufficient to answer the question, say so clearly.");
        return sb.ToString();
    }

    private static string BuildChartPrompt(string dataSeries, string chartType)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a chart generation assistant. Create a {chartType} chart from the following JSON data series.");
        sb.AppendLine("Use Python with matplotlib to generate the chart and save it as a PNG image.");
        sb.AppendLine("After generating the chart, provide a brief 1-2 sentence description of what the chart shows.");
        sb.AppendLine("Format: save the chart image, then write your description below it.");
        sb.AppendLine();
        sb.AppendLine("DATA SERIES (JSON array with 'label' and 'value' fields):");
        sb.AppendLine(dataSeries);
        sb.AppendLine();
        sb.AppendLine($"Chart type: {chartType}");
        sb.AppendLine("Chart styling: clean white background, clear axis labels, title derived from the data, professional appearance.");
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Result formatting (preserved verbatim from legacy CodeInterpreterTools so the
    // chat agent renders unchanged output)
    // ─────────────────────────────────────────────────────────────────────────────

    private static string FormatAnalysisResult(string question, CodeInterpreterResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("**Data Analysis Result**");
        sb.AppendLine($"Question: {question}");
        sb.AppendLine();
        sb.AppendLine(result.Output);

        if (!string.IsNullOrWhiteSpace(result.ExecutionLog))
        {
            sb.AppendLine();
            sb.AppendLine("*Analysis performed by Code Interpreter sandbox (Azure AI Foundry).*");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatChartResult(string chartType, CodeInterpreterResult result)
    {
        var sb = new StringBuilder();

        // Spec MUST rule: AI-generated charts MUST be labelled.
        sb.AppendLine("[AI-generated chart]");
        sb.AppendLine($"**{char.ToUpperInvariant(chartType[0])}{chartType[1..]} Chart** — generated by Code Interpreter sandbox");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(result.ChartBase64))
        {
            sb.AppendLine($"![Chart](data:image/png;base64,{result.ChartBase64})");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            sb.AppendLine(result.Output);
        }

        return sb.ToString().TrimEnd();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Citation envelopes (Wave 7b infrastructure)
    // ─────────────────────────────────────────────────────────────────────────────

    private const int MaxCitationExcerptLength = 200;

    private static ToolResultCitation BuildAnalysisCitation(string question, string output)
    {
        var excerpt = TruncateExcerpt(output);
        var snippetBase = $"Analysis of: {question}";
        var snippet = snippetBase.Length > 80 ? snippetBase[..80] : snippetBase;

        return new ToolResultCitation(
            ChunkId: $"code-interpreter-analysis-{Guid.NewGuid():N}",
            SourceName: "Code Interpreter Data Analysis",
            PageNumber: null,
            Excerpt: excerpt,
            SourceType: "code-interpreter",
            Url: null,
            Snippet: snippet);
    }

    private static ToolResultCitation BuildChartCitation(string chartType, string output)
    {
        var excerpt = TruncateExcerpt(output);

        return new ToolResultCitation(
            ChunkId: $"code-interpreter-chart-{Guid.NewGuid():N}",
            SourceName: $"AI-generated {chartType} chart",
            PageNumber: null,
            Excerpt: excerpt,
            SourceType: "code-interpreter-chart",
            Url: null,
            Snippet: $"[AI-generated chart] {chartType} chart generated by Code Interpreter sandbox");
    }

    private static string TruncateExcerpt(string output)
    {
        if (string.IsNullOrEmpty(output))
            return string.Empty;

        return output.Length > MaxCitationExcerptLength
            ? output[..MaxCitationExcerptLength] + "..."
            : output;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Argument parsing
    // ─────────────────────────────────────────────────────────────────────────────

    private static CodeInterpreterArgs ParseChatArgs(string? toolArgumentsJson)
    {
        if (string.IsNullOrWhiteSpace(toolArgumentsJson))
            return default;

        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return default;

            var root = doc.RootElement;

            return new CodeInterpreterArgs
            {
                Data = TryReadString(root, "data"),
                Question = TryReadString(root, "question"),
                DataSeries = TryReadString(root, "dataSeries"),
                ChartType = TryReadString(root, "chartType")
            };
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static bool HasNonEmptyString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var prop))
            return false;

        if (prop.ValueKind != JsonValueKind.String)
            return false;

        return !string.IsNullOrWhiteSpace(prop.GetString());
    }

    /// <summary>
    /// Read the <c>method</c> discriminator from the tool's configuration JSON. Defaults to
    /// <see cref="MethodAnalyzeData"/> when missing (the less-resource-intensive method);
    /// <see cref="ValidateChat"/> surfaces unsupported methods with a clear error.
    /// </summary>
    private static string ResolveMethod(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return MethodAnalyzeData;

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return MethodAnalyzeData;

            if (doc.RootElement.TryGetProperty("method", out var methodProp)
                && methodProp.ValueKind == JsonValueKind.String)
            {
                var v = methodProp.GetString();
                if (!string.IsNullOrWhiteSpace(v))
                    return v!;
            }
        }
        catch (JsonException)
        {
            // Fall through to default
        }

        return MethodAnalyzeData;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Result builders (kill switch + busy + cancelled + error)
    // ─────────────────────────────────────────────────────────────────────────────

    private ToolResult BuildKillSwitchResult(AnalysisTool tool, string method, DateTimeOffset startedAt)
    {
        var (message, featureLabel) = method == MethodGenerateChart
            ? ("The chart generation feature is currently unavailable. " +
               "Please contact your administrator to enable Code Interpreter (CodeInterpreter:Enabled).",
               "chart generation")
            : ("The data analysis feature is currently unavailable. " +
               "Please contact your administrator to enable Code Interpreter (CodeInterpreter:Enabled).",
               "data analysis");

        return ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: new CodeInterpreterPayload
            {
                Method = method,
                Message = message,
                Unavailable = true
            },
            summary: $"Code Interpreter {featureLabel} is unavailable (kill switch).",
            confidence: 0.0,
            execution: new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                ModelCalls = 0
            });
    }

    private ToolResult BuildBusyResult(AnalysisTool tool, string method, DateTimeOffset startedAt)
    {
        var (message, featureLabel) = method == MethodGenerateChart
            ? ("The chart generation sandbox is currently busy. " +
               "Too many concurrent requests are in progress. Please try again in a few seconds.",
               "chart generation")
            : ("The data analysis sandbox is currently busy. " +
               "Too many concurrent analysis requests are in progress. Please try again in a few seconds.",
               "data analysis");

        return ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: new CodeInterpreterPayload
            {
                Method = method,
                Message = message,
                Busy = true
            },
            summary: $"Code Interpreter {featureLabel} sandbox is at capacity.",
            confidence: 0.0,
            execution: new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                ModelCalls = 0
            });
    }

    private ToolResult BuildCancelledResult(AnalysisTool tool, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "Code Interpreter invocation was cancelled.",
            ToolErrorCodes.Cancelled,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    private ToolResult BuildInternalErrorResult(AnalysisTool tool, Exception ex, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            $"Code Interpreter invocation failed: {ex.Message}",
            ToolErrorCodes.InternalError,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    // ─────────────────────────────────────────────────────────────────────────────
    // Test seam
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Virtual indirection over the sandbox bridge call so unit tests can override it
    /// without constructing a real <see cref="CodeInterpreterBridge"/> (which is
    /// <c>sealed</c> and depends on the sealed <see cref="AgentServiceClient"/>). Production
    /// callers always go through the real bridge — this is an internal-virtual seam, not
    /// a public extension point.
    /// </summary>
    protected internal virtual Task<CodeInterpreterResult> InvokeBridgeAsync(
        string prompt,
        CancellationToken cancellationToken)
        => _bridge.InvokeCodeInterpreterAsync(prompt, cancellationToken);

    // ─────────────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parsed chat-call arguments. All four fields are nullable because the two methods
    /// use disjoint argument subsets (AnalyzeData uses data + question; GenerateChart uses
    /// dataSeries + chartType).
    /// </summary>
    private readonly record struct CodeInterpreterArgs
    {
        public string? Data { get; init; }
        public string? Question { get; init; }
        public string? DataSeries { get; init; }
        public string? ChartType { get; init; }
    }

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. Carries the
    /// method discriminator + the formatted text the chat agent renders + a small set of
    /// counts / flags for telemetry attribution.
    /// </summary>
    public sealed class CodeInterpreterPayload
    {
        /// <summary>Method that produced this payload — echo of the row's discriminator.</summary>
        [JsonPropertyName("method")]
        public string Method { get; set; } = MethodAnalyzeData;

        /// <summary>Formatted text content (markdown-style) when the sandbox returned a result.</summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>Human-readable status message — populated for kill switch / busy paths.</summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>True when the kill switch suppressed the call (ADR-018).</summary>
        [JsonPropertyName("unavailable")]
        public bool Unavailable { get; set; }

        /// <summary>True when the concurrency gate rejected the call (ADR-016).</summary>
        [JsonPropertyName("busy")]
        public bool Busy { get; set; }

        /// <summary>Length of the raw sandbox output in characters (ADR-015 telemetry shadow field).</summary>
        [JsonPropertyName("outputLength")]
        public int OutputLength { get; set; }

        /// <summary>True when the GenerateChart path produced an inline base-64 chart image.</summary>
        [JsonPropertyName("hasChart")]
        public bool HasChart { get; set; }

        /// <summary>Normalized chart type ("bar" / "line" / "pie") — only set for GenerateChart.</summary>
        [JsonPropertyName("chartType")]
        public string? ChartType { get; set; }
    }
}
