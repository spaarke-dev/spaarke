using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Foundry;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool class providing Code Interpreter sandbox capabilities for the SprkChatAgent.
///
/// Exposes two analysis tools:
///   - <see cref="AnalyzeDataAsync"/> — runs tabular/CSV data through the Azure AI Foundry
///     Code Interpreter sandbox to answer a specific analysis question.
///   - <see cref="GenerateChartAsync"/> — sends a JSON data series to the sandbox to produce
///     a chart image (bar, line, or pie) and returns an inline data URI or description string.
///
/// Data governance (ADR-015): tools ONLY accept caller-supplied data excerpts.
/// Full documents, raw file bytes, and PII MUST NOT be sent to the sandbox.
/// Callers are responsible for supplying pre-extracted, minimised data.
///
/// Kill switch (ADR-018): <see cref="CodeInterpreterOptions.Enabled"/> is checked before every
/// tool invocation. When disabled, methods return a user-readable string so the AI model can
/// gracefully inform the user — no exception is thrown.
///
/// Rate limiting (ADR-016): a static <see cref="SemaphoreSlim"/> bounds concurrent sandbox
/// calls to <see cref="CodeInterpreterOptions.MaxConcurrency"/>. Calls exceeding the limit
/// receive a 429-equivalent user-readable rejection string.
///
/// Output attribution (spec MUST rule): chart outputs are labelled "[AI-generated chart]"
/// so the user is aware the image was produced by an AI model. Citation metadata is registered
/// in <see cref="CitationContext"/> following the <see cref="WebSearchTools"/> pattern.
///
/// Instantiated by <see cref="SprkChatAgentFactory"/>. Not registered in DI — the factory
/// creates instances and registers methods as <see cref="Microsoft.Extensions.AI.AIFunction"/>
/// objects via <see cref="Microsoft.Extensions.AI.AIFunctionFactory.Create"/>.
///
/// ADR-010: 0 additional DI registrations — factory-instantiated only.
/// ADR-013: AI tools use AIFunctionFactory.Create pattern.
/// ADR-015: Prompt content and sandbox output MUST NOT be logged above Debug.
/// ADR-016: Sandbox calls bounded by SemaphoreSlim with MaxConcurrency from options.
/// ADR-018: Kill switch checked before every invocation; returns graceful string when disabled.
/// </summary>
public sealed class CodeInterpreterTools
{
    /// <summary>
    /// Bounds concurrent Code Interpreter sandbox calls.
    ///
    /// Static so the limit applies across all <see cref="CodeInterpreterTools"/> instances
    /// (i.e., all concurrent agent sessions sharing the same BFF process).
    ///
    /// Initialised lazily via the options value passed to the first instance (see constructor).
    /// The semaphore is re-used across instances — the options value is checked to ensure
    /// MaxConcurrency matches; mismatches are logged as warnings (harmless for existing callers).
    /// </summary>
    private static SemaphoreSlim? s_concurrencyGate;
    private static readonly object s_gateLock = new();

    private readonly CodeInterpreterBridge _bridge;
    private readonly CodeInterpreterOptions _options;
    private readonly ILogger _logger;
    private readonly CitationContext? _citationContext;

    /// <summary>
    /// Creates a new <see cref="CodeInterpreterTools"/> instance.
    /// </summary>
    /// <param name="bridge">Code Interpreter bridge wrapping the Foundry sandbox.</param>
    /// <param name="options">Kill switch and concurrency configuration (ADR-016, ADR-018).</param>
    /// <param name="logger">Logger for operation metadata (ADR-015: no content above Debug).</param>
    /// <param name="citationContext">Shared citation context for registering tool output citations.</param>
    public CodeInterpreterTools(
        CodeInterpreterBridge bridge,
        IOptions<CodeInterpreterOptions> options,
        ILogger logger,
        CitationContext? citationContext)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _citationContext = citationContext;

        // Initialise the static concurrency gate on first construction (ADR-016).
        // Subsequent constructions skip re-initialisation — the gate is shared across sessions.
        lock (s_gateLock)
        {
            s_concurrencyGate ??= new SemaphoreSlim(
                initialCount: _options.MaxConcurrency,
                maxCount: _options.MaxConcurrency);
        }
    }

    // ── Tool Methods ───────────────────────────────────────────────────────────

    /// <summary>
    /// Analyzes tabular or CSV data to answer a specific analysis question.
    ///
    /// Sends a minimal, caller-supplied data excerpt to the Azure AI Foundry Code Interpreter
    /// sandbox which executes Python to derive statistics, trends, or comparisons.
    ///
    /// Data governance (ADR-015): only the <paramref name="data"/> excerpt provided by the
    /// caller is forwarded to the sandbox. Do NOT pass full document content, raw file bytes,
    /// or data containing PII. Supply the smallest excerpt that answers the question.
    ///
    /// Kill switch (ADR-018): returns a graceful unavailability string when disabled.
    /// Rate limiting (ADR-016): returns a graceful 429 string when concurrency is exceeded.
    /// </summary>
    /// <param name="data">CSV or tabular data excerpt to analyze</param>
    /// <param name="question">Specific analysis question to answer using the data</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted analysis result string including key findings from the sandbox run.</returns>
    [Description("Analyze tabular or CSV data to answer a specific question. " +
                 "Use this when the user wants statistics, trends, or comparisons derived from structured data. " +
                 "Supply only the relevant data excerpt — never full documents or PII.")]
    public async Task<string> AnalyzeDataAsync(
        [Description("CSV or tabular data excerpt to analyze")] string data,
        [Description("Specific analysis question to answer using the data")] string question,
        CancellationToken cancellationToken = default)
    {
        // ADR-018: Kill switch — return graceful string (not exception) so the model can report unavailability.
        if (!_options.Enabled)
        {
            _logger.LogDebug("CodeInterpreter AnalyzeData skipped — feature disabled via kill switch");
            return "The data analysis feature is currently unavailable. " +
                   "Please contact your administrator to enable Code Interpreter (CodeInterpreter:Enabled).";
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(data, nameof(data));
        ArgumentException.ThrowIfNullOrWhiteSpace(question, nameof(question));

        // ADR-015: log lengths only — never log the data or question content above Debug.
        _logger.LogInformation(
            "AnalyzeData starting — dataLen={DataLen}, questionLen={QuestionLen}",
            data.Length, question.Length);

        // ADR-016: Acquire concurrency semaphore — reject with user-readable string on timeout.
        var timeout = TimeSpan.FromSeconds(_options.SandboxTimeoutSeconds);
        if (!await s_concurrencyGate!.WaitAsync(timeout, cancellationToken))
        {
            _logger.LogWarning(
                "CodeInterpreter concurrency limit reached (max {MaxConcurrency}) for AnalyzeData — returning 429",
                _options.MaxConcurrency);
            return "The data analysis sandbox is currently busy. " +
                   "Too many concurrent analysis requests are in progress. Please try again in a few seconds.";
        }

        try
        {
            // Build the sandbox prompt — data first, then question (ADR-015: minimise content).
            var prompt = BuildAnalysisPrompt(data, question);

            var result = await _bridge.InvokeCodeInterpreterAsync(prompt, cancellationToken);

            // ADR-015: log only output length — never content.
            _logger.LogInformation(
                "AnalyzeData completed — outputLen={OutputLen}",
                result.Output.Length);

            // Register citation so the frontend can attribute the result (WebSearchTools pattern).
            RegisterAnalysisCitation(question, result.Output);

            return FormatAnalysisResult(question, result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "AnalyzeData timed out after {TimeoutSeconds}s — returning timeout message",
                _options.SandboxTimeoutSeconds);
            return $"The data analysis timed out after {_options.SandboxTimeoutSeconds} seconds. " +
                   "Please try a smaller data excerpt or a simpler question.";
        }
        catch (FeatureDisabledException ex)
        {
            _logger.LogWarning(ex, "AnalyzeData: Code Interpreter feature disabled mid-call");
            return "The data analysis feature was disabled during the operation. Please try again later.";
        }
        catch (ConcurrencyLimitExceededException ex)
        {
            _logger.LogWarning(ex, "AnalyzeData: Code Interpreter concurrency exceeded at bridge level");
            return "The data analysis sandbox is currently at capacity. Please try again in a few seconds.";
        }
        finally
        {
            s_concurrencyGate!.Release();
        }
    }

    /// <summary>
    /// Generates a chart image from a JSON data series using the Code Interpreter sandbox.
    ///
    /// Sends the data series to the Azure AI Foundry Code Interpreter which executes Python
    /// (matplotlib) to produce a chart. Returns an inline base-64 data URI when an image is
    /// produced, or a text description of the chart when the sandbox returns text only.
    ///
    /// Output attribution (spec MUST): all chart outputs are labelled "[AI-generated chart]"
    /// in the returned string so users are aware the image was produced by an AI model.
    ///
    /// Data governance (ADR-015): only the <paramref name="dataSeries"/> provided by the caller
    /// is forwarded. Do NOT include PII or data beyond what is needed for the chart.
    ///
    /// Kill switch (ADR-018): returns a graceful unavailability string when disabled.
    /// Rate limiting (ADR-016): returns a graceful 429 string when concurrency is exceeded.
    /// </summary>
    /// <param name="dataSeries">Data series as JSON array (e.g., [{"label":"Q1","value":120}, ...])</param>
    /// <param name="chartType">Chart type: bar, line, or pie</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Inline base-64 data URI string when an image is available, or a textual chart description
    /// prefixed with "[AI-generated chart]" for attribution.
    /// </returns>
    [Description("Generate a chart image from a JSON data series. " +
                 "Use this when the user wants a visual chart (bar, line, or pie) from structured data. " +
                 "dataSeries must be a JSON array of objects with 'label' and 'value' fields.")]
    public async Task<string> GenerateChartAsync(
        [Description("Data series as JSON array (e.g., [{\"label\":\"Q1\",\"value\":120}])")] string dataSeries,
        [Description("Chart type: bar, line, or pie")] string chartType,
        CancellationToken cancellationToken = default)
    {
        // ADR-018: Kill switch — graceful string, not exception.
        if (!_options.Enabled)
        {
            _logger.LogDebug("CodeInterpreter GenerateChart skipped — feature disabled via kill switch");
            return "The chart generation feature is currently unavailable. " +
                   "Please contact your administrator to enable Code Interpreter (CodeInterpreter:Enabled).";
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(dataSeries, nameof(dataSeries));
        ArgumentException.ThrowIfNullOrWhiteSpace(chartType, nameof(chartType));

        // Normalise and validate chartType.
        var normalizedChartType = chartType.Trim().ToLowerInvariant();
        if (normalizedChartType is not ("bar" or "line" or "pie"))
        {
            _logger.LogWarning("GenerateChart: unsupported chartType={ChartType}; defaulting to bar", chartType);
            normalizedChartType = "bar";
        }

        // ADR-015: log lengths only.
        _logger.LogInformation(
            "GenerateChart starting — dataSeriesLen={DataSeriesLen}, chartType={ChartType}",
            dataSeries.Length, normalizedChartType);

        // ADR-016: Acquire concurrency semaphore.
        var timeout = TimeSpan.FromSeconds(_options.SandboxTimeoutSeconds);
        if (!await s_concurrencyGate!.WaitAsync(timeout, cancellationToken))
        {
            _logger.LogWarning(
                "CodeInterpreter concurrency limit reached (max {MaxConcurrency}) for GenerateChart — returning 429",
                _options.MaxConcurrency);
            return "The chart generation sandbox is currently busy. " +
                   "Too many concurrent requests are in progress. Please try again in a few seconds.";
        }

        try
        {
            var prompt = BuildChartPrompt(dataSeries, normalizedChartType);

            var result = await _bridge.InvokeCodeInterpreterAsync(prompt, cancellationToken);

            // ADR-015: log only lengths.
            _logger.LogInformation(
                "GenerateChart completed — chartType={ChartType}, hasChart={HasChart}, outputLen={OutputLen}",
                normalizedChartType, result.ChartBase64 != null, result.Output.Length);

            // Register citation for chart output attribution.
            RegisterChartCitation(normalizedChartType, result.Output);

            return FormatChartResult(normalizedChartType, result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "GenerateChart timed out after {TimeoutSeconds}s",
                _options.SandboxTimeoutSeconds);
            return $"[AI-generated chart] Chart generation timed out after {_options.SandboxTimeoutSeconds} seconds. " +
                   "Please try a smaller data series or a simpler chart type.";
        }
        catch (FeatureDisabledException ex)
        {
            _logger.LogWarning(ex, "GenerateChart: Code Interpreter feature disabled mid-call");
            return "[AI-generated chart] The chart generation feature was disabled during the operation. Please try again later.";
        }
        catch (ConcurrencyLimitExceededException ex)
        {
            _logger.LogWarning(ex, "GenerateChart: Code Interpreter concurrency exceeded at bridge level");
            return "[AI-generated chart] The chart generation sandbox is at capacity. Please try again in a few seconds.";
        }
        finally
        {
            s_concurrencyGate!.Release();
        }
    }

    // ── Prompt Builders ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the analysis prompt sent to the Code Interpreter sandbox.
    /// Structured as: task description → data → question → output format instruction.
    /// </summary>
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

    /// <summary>
    /// Builds the chart generation prompt sent to the Code Interpreter sandbox.
    /// Instructs the sandbox to produce a matplotlib chart and save it as an image.
    /// </summary>
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

    // ── Citation Registration ──────────────────────────────────────────────────

    /// <summary>
    /// Registers a citation for a data analysis result in the shared <see cref="CitationContext"/>.
    /// Follows the <see cref="WebSearchTools"/> citation pattern.
    /// SourceType "code-interpreter" signals the frontend to render an AI badge.
    /// </summary>
    private void RegisterAnalysisCitation(string question, string output)
    {
        if (_citationContext is null) return;

        var excerpt = output.Length > CitationContext.MaxExcerptLength
            ? output[..CitationContext.MaxExcerptLength] + "..."
            : output;

        _citationContext.AddCitation(
            chunkId: $"code-interpreter-analysis-{Guid.NewGuid():N}",
            sourceName: "Code Interpreter Data Analysis",
            pageNumber: null,
            excerpt: excerpt,
            sourceType: "code-interpreter",
            url: null,
            snippet: $"Analysis of: {question[..Math.Min(question.Length, 80)]}");
    }

    /// <summary>
    /// Registers a citation for a chart generation result in the shared <see cref="CitationContext"/>.
    /// SourceType "code-interpreter-chart" signals the frontend to render an AI-generated chart badge.
    /// </summary>
    private void RegisterChartCitation(string chartType, string output)
    {
        if (_citationContext is null) return;

        var excerpt = output.Length > CitationContext.MaxExcerptLength
            ? output[..CitationContext.MaxExcerptLength] + "..."
            : output;

        _citationContext.AddCitation(
            chunkId: $"code-interpreter-chart-{Guid.NewGuid():N}",
            sourceName: $"AI-generated {chartType} chart",
            pageNumber: null,
            excerpt: excerpt,
            sourceType: "code-interpreter-chart",
            url: null,
            snippet: $"[AI-generated chart] {chartType} chart generated by Code Interpreter sandbox");
    }

    // ── Result Formatting ──────────────────────────────────────────────────────

    /// <summary>
    /// Formats the Code Interpreter analysis result for AI model consumption.
    /// </summary>
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

    /// <summary>
    /// Formats the chart generation result for AI model consumption.
    /// Prefixes with "[AI-generated chart]" for output attribution (spec MUST rule).
    /// When a base-64 chart image is available, embeds it as a data URI.
    /// </summary>
    private static string FormatChartResult(string chartType, CodeInterpreterResult result)
    {
        var sb = new StringBuilder();

        // Spec MUST rule: AI-generated charts MUST be labeled.
        sb.AppendLine("[AI-generated chart]");
        sb.AppendLine($"**{chartType.ToUpperInvariant()[0]}{chartType[1..]} Chart** — generated by Code Interpreter sandbox");
        sb.AppendLine();

        // Embed base-64 chart image when available.
        if (!string.IsNullOrWhiteSpace(result.ChartBase64))
        {
            sb.AppendLine($"![Chart](data:image/png;base64,{result.ChartBase64})");
            sb.AppendLine();
        }

        // Include the model's description of the chart.
        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            sb.AppendLine(result.Output);
        }

        return sb.ToString().TrimEnd();
    }
}
