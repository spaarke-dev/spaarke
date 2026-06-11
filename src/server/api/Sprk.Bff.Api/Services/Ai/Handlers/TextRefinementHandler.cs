using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Typed <see cref="IToolHandler"/> implementation of text refinement / key-point extraction /
/// summary generation (R6 Pillar 2 Wave 7 Q9 migration of <c>TextRefinementTools</c>).
/// </summary>
/// <remarks>
/// <para>
/// Multiplexes three LLM-assisted single-turn methods over the typed-handler contract:
/// </para>
/// <list type="bullet">
/// <item><c>RefineText(text, instruction)</c> — reformat / improve clarity per an instruction.</item>
/// <item><c>ExtractKeyPoints(text, maxPoints)</c> — extract top N bullet points.</item>
/// <item><c>GenerateSummary(text, format)</c> — summarise as bullet / paragraph / tldr.</item>
/// </list>
/// <para>
/// <strong>Method dispatch</strong>: three separate <c>sprk_analysistool</c> rows
/// (<c>TEXT-REFINE</c>, <c>TEXT-KEYPOINTS</c>, <c>TEXT-SUMMARY</c>) all point at this same
/// <c>sprk_handlerclass = TextRefinementHandler</c>. Each row's <c>sprk_configuration</c>
/// JSON carries a <c>method</c> discriminator (<c>"refine" | "keypoints" | "summary"</c>).
/// The handler reads the discriminator at execution time and routes to the corresponding
/// internal method. This gives the LLM three distinct tools (each with its own description
/// + input shape) while keeping a single handler class — matching the pre-R6 exposure model
/// that hand-registered three <c>AIFunction</c>s from one <c>TextRefinementTools</c> instance.
/// </para>
/// <para>
/// <strong>Pipeline</strong>: input args → method-discriminator dispatch →
/// focused single-turn prompt (system + user) → <see cref="IChatClient.GetResponseAsync"/>
/// → <see cref="ToolResult"/> with the model's text in <c>data.text</c>.
/// No structured-outputs schema (output is plain prose / bullet markdown).
/// </para>
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Both"/>. Playbook
/// path consumes the document's extracted text plus the
/// <see cref="ToolExecutionContext.ActionSystemPrompt"/> as the instruction; chat path reads
/// arguments from <see cref="ChatInvocationContext.ToolArgumentsJson"/>.
/// </para>
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered via assembly scan; ZERO manual DI line.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; CRUD-side code
/// routes through <c>PublicContracts</c> facades, never directly into this handler.</item>
/// <item><strong>ADR-014</strong>: no handler-side cache layer — pre-R6
/// <c>TextRefinementTools</c> did not cache; <see cref="IChatClient"/>'s downstream caching
/// (if any) is preserved unchanged.</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + IDs + method discriminator
/// + outcome + duration ONLY. NEVER input text, NEVER the instruction, NEVER output content.</item>
/// <item><strong>ADR-016</strong>: rate-limiting is inherited via the
/// <c>SprkChatAgent</c> path that ultimately invokes the typed-handler adapter — the LLM
/// call here goes through the same <see cref="IChatClient"/> as the rest of the chat agent.</item>
/// <item><strong>ADR-029</strong>: zero new NuGet packages; per-handler delta target ≤+0.5 MB.</item>
/// </list>
/// </remarks>
public sealed class TextRefinementHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(TextRefinementHandler);

    private const string MethodRefine = "refine";
    private const string MethodKeypoints = "keypoints";
    private const string MethodSummary = "summary";

    private static readonly HashSet<string> SupportedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        MethodRefine, MethodKeypoints, MethodSummary
    };

    private static readonly HashSet<string> SupportedSummaryFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "bullet", "paragraph", "tldr"
    };

    private readonly IChatClient _chatClient;
    private readonly ILogger<TextRefinementHandler> _logger;

    public TextRefinementHandler(
        IChatClient chatClient,
        ILogger<TextRefinementHandler> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Text Refinement",
        Description: "LLM-assisted text refinement: refine/reformat per instruction, extract key points, " +
                     "or generate a concise summary (bullet/paragraph/tldr). Single handler multiplexes " +
                     "three methods via the 'method' discriminator in sprk_configuration.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition("text", "Input text to operate on.", ToolParameterType.String, Required: true),
            new ToolParameterDefinition("instruction", "Refinement instruction (refine method only).", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("maxPoints", "Max number of key points (keypoints method only). Default 5.", ToolParameterType.Integer, Required: false, DefaultValue: 5),
            new ToolParameterDefinition("format", "Summary format (summary method only): bullet | paragraph | tldr.", ToolParameterType.String, Required: false, DefaultValue: "bullet")
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Both;

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        if (context.Document is null)
            return ToolValidationResult.Failure("Document context is required.");

        if (string.IsNullOrWhiteSpace(context.Document.ExtractedText))
            return ToolValidationResult.Failure("Document extracted text is required.");

        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        return ValidateConfiguration(tool.Configuration);
    }

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

            if (!doc.RootElement.TryGetProperty("text", out var textProp) ||
                textProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(textProp.GetString()))
            {
                return ToolValidationResult.Failure("Tool arguments must include a non-empty 'text' string field.");
            }
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Failure($"Tool arguments JSON is malformed: {ex.Message}");
        }

        return ValidateConfiguration(tool.Configuration);
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            var method = ResolveMethod(tool.Configuration);

            // ADR-015: log handler + IDs + method only — never input text or instruction
            _logger.LogInformation(
                "TextRefinementHandler executing method '{Method}' for analysis {AnalysisId}, tool {ToolId}",
                method, context.AnalysisId, tool.Id);

            cancellationToken.ThrowIfCancellationRequested();

            // Playbook path: text comes from document extracted text. Instruction (for refine)
            // is sourced from the Action's system prompt (Option A: Action = what to do).
            // Format / maxPoints fall back to defaults from configuration.
            var args = new InvocationArgs
            {
                Text = context.Document!.ExtractedText ?? string.Empty,
                Instruction = context.ActionSystemPrompt,
                MaxPoints = ReadIntFromConfig(tool.Configuration, "maxPoints", defaultValue: 5),
                Format = ReadStringFromConfig(tool.Configuration, "format") ?? "bullet"
            };

            return await DispatchAndReturnAsync(
                method,
                args,
                tool,
                startedAt: startedAt,
                stopwatch: stopwatch,
                correlationLogId: context.AnalysisId.ToString(),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "TextRefinementHandler cancelled for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Text refinement was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            // ADR-015: log only the exception TYPE — no input/response echo
            _logger.LogError(
                ex,
                "TextRefinementHandler failed for analysis {AnalysisId}: {ErrorType}",
                context.AnalysisId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Text refinement failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            var method = ResolveMethod(tool.Configuration);

            _logger.LogInformation(
                "TextRefinementHandler chat-invocation method '{Method}' for session {ChatSessionId}, decision {DecisionId}",
                method, context.ChatSessionId, context.DecisionId);

            cancellationToken.ThrowIfCancellationRequested();

            var args = ParseChatArgs(context.ToolArgumentsJson ?? "{}", tool.Configuration);

            return await DispatchAndReturnAsync(
                method,
                args,
                tool,
                startedAt: startedAt,
                stopwatch: stopwatch,
                correlationLogId: $"session={context.ChatSessionId},decision={context.DecisionId}",
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "TextRefinementHandler chat cancelled for session {ChatSessionId}, decision {DecisionId}",
                context.ChatSessionId, context.DecisionId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Text refinement was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "TextRefinementHandler chat failed for session {ChatSessionId}, decision {DecisionId}: {ErrorType}",
                context.ChatSessionId, context.DecisionId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Text refinement failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Core dispatch
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> DispatchAndReturnAsync(
        string method,
        InvocationArgs args,
        AnalysisTool tool,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(args.Text))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Input 'text' is required.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        var messages = method switch
        {
            MethodRefine => BuildRefineMessages(args.Text, args.Instruction ?? string.Empty),
            MethodKeypoints => BuildKeypointsMessages(args.Text, args.MaxPoints),
            MethodSummary => BuildSummaryMessages(args.Text, args.Format),
            _ => throw new InvalidOperationException($"Unsupported method '{method}'.")
        };

        // Special validation: refine requires instruction
        if (method == MethodRefine && string.IsNullOrWhiteSpace(args.Instruction))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Input 'instruction' is required for method 'refine'.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        var response = await _chatClient.GetResponseAsync(
            messages, cancellationToken: cancellationToken);

        var resultText = response.Text ?? string.Empty;

        stopwatch.Stop();

        var executionMetadata = new ToolExecutionMetadata
        {
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            CacheHit = false,
            ModelCalls = 1,
            ModelName = null,
            InputTokens = EstimateTokens(args.Text) + (args.Instruction is null ? 0 : EstimateTokens(args.Instruction)),
            OutputTokens = EstimateTokens(resultText)
        };

        // ADR-015: log method + outcome + duration + output-length bucket ONLY.
        // Never input text, never output text, never instruction.
        _logger.LogInformation(
            "TextRefinementHandler complete ({Correlation}): method='{Method}', outputLengthBucket={OutputBucket}, in {Duration}ms",
            correlationLogId,
            method,
            BucketLength(resultText.Length),
            stopwatch.ElapsedMilliseconds);

        return ToolResult.Ok(
            HandlerId,
            tool.Id,
            tool.Name,
            data: new TextRefinementResult { Method = method, Text = resultText },
            summary: $"Text refinement '{method}' produced {BucketLength(resultText.Length)} output.",
            confidence: string.IsNullOrEmpty(resultText) ? 0.0 : 1.0,
            execution: executionMetadata);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Method-specific prompt builders (mirror pre-R6 TextRefinementTools shape)
    // ─────────────────────────────────────────────────────────────────────────────

    private static List<ChatMessage> BuildRefineMessages(string text, string instruction)
    {
        var systemPrompt =
            "You are a professional editor. Apply the user's instruction to refine the provided text. " +
            "Output only the refined text — no explanation, preamble, or meta-commentary.";

        var userPrompt = $"Instruction: {instruction}\n\nText to refine:\n{text}";

        return
        [
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, userPrompt)
        ];
    }

    private static List<ChatMessage> BuildKeypointsMessages(string text, int maxPoints)
    {
        var count = Math.Clamp(maxPoints, 1, 20);

        return
        [
            new ChatMessage(ChatRole.System,
                $"You are an expert analyst. Extract exactly the {count} most important key points from the provided text. " +
                $"Format each point as a bullet (- ) on its own line. " +
                $"Be concise and factual. Output only the bullet points — no introduction or closing."),
            new ChatMessage(ChatRole.User, text)
        ];
    }

    private static List<ChatMessage> BuildSummaryMessages(string text, string format)
    {
        var fmt = string.IsNullOrWhiteSpace(format) ? "bullet" : format.ToLowerInvariant();
        if (!SupportedSummaryFormats.Contains(fmt))
            fmt = "bullet";

        var formatInstruction = fmt switch
        {
            "paragraph" => "Write a single concise paragraph (3–5 sentences) summarising the key content.",
            "tldr" => "Write a TL;DR in 1–2 sentences. Start with 'TL;DR: '.",
            _ => "Summarise the key content as a bullet list (use - for each bullet). Be concise and informative."
        };

        return
        [
            new ChatMessage(ChatRole.System,
                $"You are an expert summariser. {formatInstruction} " +
                $"Output only the summary — no introduction, preamble, or closing remarks."),
            new ChatMessage(ChatRole.User, text)
        ];
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Configuration parsing — method discriminator + optional defaults
    // ─────────────────────────────────────────────────────────────────────────────

    private static ToolValidationResult ValidateConfiguration(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return ToolValidationResult.Failure(
                "Configuration JSON is required and must include 'method' = 'refine' | 'keypoints' | 'summary'.");

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ToolValidationResult.Failure("Configuration must be a JSON object.");

            if (!doc.RootElement.TryGetProperty("method", out var methodProp) ||
                methodProp.ValueKind != JsonValueKind.String)
            {
                return ToolValidationResult.Failure(
                    "Configuration must include 'method' as a string ('refine' | 'keypoints' | 'summary').");
            }

            var method = methodProp.GetString();
            if (string.IsNullOrWhiteSpace(method) || !SupportedMethods.Contains(method))
            {
                return ToolValidationResult.Failure(
                    $"Configuration 'method' must be one of: {string.Join(", ", SupportedMethods)}.");
            }

            // Optional 'format' must be in the allow-list when present.
            if (doc.RootElement.TryGetProperty("format", out var formatProp) &&
                formatProp.ValueKind == JsonValueKind.String)
            {
                var fmt = formatProp.GetString();
                if (!string.IsNullOrWhiteSpace(fmt) && !SupportedSummaryFormats.Contains(fmt))
                {
                    return ToolValidationResult.Failure(
                        $"Configuration 'format' must be one of: {string.Join(", ", SupportedSummaryFormats)}.");
                }
            }

            return ToolValidationResult.Success();
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Failure($"Configuration JSON parse error: {ex.Message}");
        }
    }

    private static string ResolveMethod(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            throw new InvalidOperationException("Configuration JSON is required to resolve the method discriminator.");

        using var doc = JsonDocument.Parse(configurationJson);
        if (!doc.RootElement.TryGetProperty("method", out var methodProp) ||
            methodProp.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Configuration must include 'method' as a string.");
        }

        var method = methodProp.GetString()?.ToLowerInvariant() ?? string.Empty;
        if (!SupportedMethods.Contains(method))
            throw new InvalidOperationException($"Unsupported method '{method}'.");

        return method;
    }

    private static int ReadIntFromConfig(string? configurationJson, string field, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(configurationJson)) return defaultValue;
        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            if (doc.RootElement.TryGetProperty(field, out var prop) &&
                prop.ValueKind == JsonValueKind.Number &&
                prop.TryGetInt32(out var v))
            {
                return v;
            }
        }
        catch (JsonException) { }
        return defaultValue;
    }

    private static string? ReadStringFromConfig(string? configurationJson, string field)
    {
        if (string.IsNullOrWhiteSpace(configurationJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            if (doc.RootElement.TryGetProperty(field, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }

    private static InvocationArgs ParseChatArgs(string toolArgumentsJson, string? configurationJson)
    {
        var args = new InvocationArgs
        {
            MaxPoints = ReadIntFromConfig(configurationJson, "maxPoints", defaultValue: 5),
            Format = ReadStringFromConfig(configurationJson, "format") ?? "bullet"
        };

        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return args;

            if (doc.RootElement.TryGetProperty("text", out var textProp) &&
                textProp.ValueKind == JsonValueKind.String)
            {
                args.Text = textProp.GetString() ?? string.Empty;
            }

            if (doc.RootElement.TryGetProperty("instruction", out var instProp) &&
                instProp.ValueKind == JsonValueKind.String)
            {
                args.Instruction = instProp.GetString();
            }

            if (doc.RootElement.TryGetProperty("maxPoints", out var mpProp) &&
                mpProp.ValueKind == JsonValueKind.Number &&
                mpProp.TryGetInt32(out var mp))
            {
                args.MaxPoints = mp;
            }

            if (doc.RootElement.TryGetProperty("format", out var fmtProp) &&
                fmtProp.ValueKind == JsonValueKind.String)
            {
                args.Format = fmtProp.GetString() ?? args.Format;
            }
        }
        catch (JsonException)
        {
            // Tolerated — empty args fall through to dispatch which will return ValidationFailed.
        }

        return args;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-015 telemetry helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bucket output length to "0" / "&lt;100" / "100-500" / "500-2000" / "2000+" so logs
    /// convey magnitude without leaking exact character counts that could be correlated
    /// with input content (ADR-015).
    /// </summary>
    private static string BucketLength(int length) => length switch
    {
        0 => "0",
        < 100 => "<100",
        < 500 => "100-500",
        < 2000 => "500-2000",
        _ => "2000+"
    };

    private static int EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);

    // ─────────────────────────────────────────────────────────────────────────────
    // Internal types
    // ─────────────────────────────────────────────────────────────────────────────

    private sealed class InvocationArgs
    {
        public string Text { get; set; } = string.Empty;
        public string? Instruction { get; set; }
        public int MaxPoints { get; set; } = 5;
        public string Format { get; set; } = "bullet";
    }

    /// <summary>
    /// Structured output: the produced text + method discriminator. Returned in
    /// <see cref="ToolResult.Data"/>.
    /// </summary>
    public sealed class TextRefinementResult
    {
        public string Method { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
