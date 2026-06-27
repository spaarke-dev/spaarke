using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-only typed handler that retrieves prior analysis results for a document (R6 Wave 7).
/// Replaces the legacy hardcoded <c>AnalysisQueryTools</c> class previously registered in
/// <c>SprkChatAgentFactory.ResolveTools</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One handler, two methods (Option b)</strong>: a single <c>sprk_analysistool</c> row
/// exposes this handler as ONE LLM tool with a <c>method</c> discriminator parameter:
/// </para>
/// <list type="bullet">
/// <item><c>method = "GetAnalysisResult"</c> → full analysis output for the document.</item>
/// <item><c>method = "GetAnalysisSummary"</c> → executive summary extracted from analysis output.</item>
/// </list>
/// <para>
/// <strong>Rationale for Option (b)</strong>: The shared <c>Seed-TypedHandlers.ps1</c> uses
/// <c>sprk_handlerclass</c> as the upsert key with a <c>SYS-%</c> name safety filter. Two rows
/// sharing the same handler class would collide on the first-match query; either both rows would
/// fail to deploy idempotently or the script would need a composite-key extension (which 3 sibling
/// Wave 7 agents are also editing). Option (b) keeps the seed shape unchanged, preserves chat
/// behavior (the LLM picks <c>method: "GetAnalysisResult"</c> vs <c>"GetAnalysisSummary"</c> from
/// the schema's enum + description — same information surface as picking between two tool names),
/// and ships one cleanly-described tool. The legacy <c>AnalysisQueryTools</c> class also exposed
/// the two paths with no captured state beyond <c>IAnalysisOrchestrationService</c> + tenant —
/// trivial to fold into one dispatcher.
/// </para>
/// <para>
/// <strong>Dependencies</strong>: <see cref="IAnalysisOrchestrationService"/> only. Resolved via
/// constructor injection (auto-discovered by <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c>).
/// </para>
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Both"/> per FR-12.
/// Playbook context is exposed for future playbook-orchestrated analysis lookups; today the
/// primary call site is the chat agent.
/// </para>
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered; ZERO manual DI line.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; CRUD-side code
/// routes through <c>PublicContracts</c> facades, never directly into this handler.</item>
/// <item><strong>ADR-014</strong>: per-tenant safety — handler validates <c>TenantId</c> on both
/// playbook and chat paths. <c>IAnalysisOrchestrationService.GetAnalysisAsync</c> is keyed by
/// analysis GUID; tenant isolation is enforced at the orchestration-service layer.</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + outcome + IDs + duration ONLY.
/// NEVER analysis content, document name as data, or working-document body.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler publish-size delta ≤+0.1 MB.</item>
/// </list>
/// </remarks>
public sealed class AnalysisQueryHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(AnalysisQueryHandler);
    private const string MethodGetAnalysisResult = "GetAnalysisResult";
    private const string MethodGetAnalysisSummary = "GetAnalysisSummary";

    private static readonly HashSet<string> SupportedMethods = new(StringComparer.Ordinal)
    {
        MethodGetAnalysisResult,
        MethodGetAnalysisSummary
    };

    private readonly IAnalysisOrchestrationService _analysisService;
    private readonly ILogger<AnalysisQueryHandler> _logger;

    public AnalysisQueryHandler(
        IAnalysisOrchestrationService analysisService,
        ILogger<AnalysisQueryHandler> logger)
    {
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Analysis Query",
        Description: "Retrieves prior analysis results for a document. Provides two methods: " +
                     "'GetAnalysisResult' returns the full analysis (working document, status, " +
                     "token usage, timestamps); 'GetAnalysisSummary' returns an executive summary " +
                     "(extracted Executive Summary section or first ~600 characters when none).",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition("documentId", "Analysis or document ID (GUID) to retrieve.", ToolParameterType.String, Required: true),
            new ToolParameterDefinition("method", "Which retrieval method: 'GetAnalysisResult' (full) or 'GetAnalysisSummary' (executive summary). Defaults to 'GetAnalysisResult'.", ToolParameterType.String, Required: false, DefaultValue: MethodGetAnalysisResult)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Both;

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

        try
        {
            using var doc = JsonDocument.Parse(context.ToolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ToolValidationResult.Failure("Tool arguments must be a JSON object.");

            if (!doc.RootElement.TryGetProperty("documentId", out var idProp) ||
                idProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(idProp.GetString()))
            {
                return ToolValidationResult.Failure("Tool arguments must include a non-empty 'documentId' string field.");
            }

            // Method is optional; default = GetAnalysisResult. If supplied, must be one of the
            // two known values — guards against LLM hallucinating a third method.
            if (doc.RootElement.TryGetProperty("method", out var methodProp))
            {
                if (methodProp.ValueKind != JsonValueKind.String)
                    return ToolValidationResult.Failure("'method' must be a string when provided.");

                var methodVal = methodProp.GetString();
                if (!string.IsNullOrWhiteSpace(methodVal) && !SupportedMethods.Contains(methodVal))
                {
                    return ToolValidationResult.Failure(
                        $"'method' '{methodVal}' is not supported. Use 'GetAnalysisResult' or 'GetAnalysisSummary'.");
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
    /// Playbook-context path. Reads <c>documentId</c> + optional <c>method</c> from the tool's
    /// <see cref="AnalysisTool.Configuration"/> JSON. Today the primary call site is chat; the
    /// playbook overload is provided for future playbook-orchestrated analysis lookups (FR-12).
    /// </remarks>
    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            _logger.LogInformation(
                "AnalysisQueryHandler executing for analysis {AnalysisId}, tool {ToolId}",
                context.AnalysisId, tool.Id);

            cancellationToken.ThrowIfCancellationRequested();

            var (documentId, method) = ParsePlaybookArgs(tool.Configuration);
            return await DispatchAsync(
                tool,
                documentId: documentId,
                method: method,
                startedAt: startedAt,
                stopwatch: stopwatch,
                correlationLogId: context.AnalysisId.ToString(),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("AnalysisQueryHandler cancelled for analysis {AnalysisId}", context.AnalysisId);
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "AnalysisQueryHandler failed for analysis {AnalysisId}: {ErrorType}",
                context.AnalysisId, ex.GetType().Name);
            return BuildInternalErrorResult(tool, ex, startedAt);
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
            _logger.LogInformation(
                "AnalysisQueryHandler chat-invocation for session {ChatSessionId}, decision {DecisionId}",
                context.ChatSessionId, context.DecisionId);

            cancellationToken.ThrowIfCancellationRequested();

            var (documentId, method) = ParseChatArgs(context.ToolArgumentsJson);

            return await DispatchAsync(
                tool,
                documentId: documentId,
                method: method,
                startedAt: startedAt,
                stopwatch: stopwatch,
                correlationLogId: $"session={context.ChatSessionId},decision={context.DecisionId}",
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "AnalysisQueryHandler chat cancelled for session {ChatSessionId}, decision {DecisionId}",
                context.ChatSessionId, context.DecisionId);
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "AnalysisQueryHandler chat failed for session {ChatSessionId}, decision {DecisionId}: {ErrorType}",
                context.ChatSessionId, context.DecisionId, ex.GetType().Name);
            return BuildInternalErrorResult(tool, ex, startedAt);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Dispatcher
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> DispatchAsync(
        AnalysisTool tool,
        string? documentId,
        string method,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "documentId is required.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        if (!Guid.TryParse(documentId, out var analysisGuid))
        {
            stopwatch.Stop();
            // ADR-015: documentId is a deterministic ID (or a malformed user input); we echo it
            // back to the caller's tool channel only, NOT into telemetry. Log uses correlationLogId.
            _logger.LogInformation(
                "AnalysisQueryHandler ({Correlation}) invalid-id outcome in {Duration}ms",
                correlationLogId, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new AnalysisQueryPayload
                {
                    Method = method,
                    Message = $"Invalid analysis ID format: '{documentId}'. Expected a GUID.",
                    Found = false
                },
                summary: $"Invalid analysis ID format: '{documentId}'. Expected a GUID.",
                confidence: 0.0,
                execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        // Default method = GetAnalysisResult when null/empty (validation already enforced
        // membership when provided).
        var effectiveMethod = string.IsNullOrWhiteSpace(method) ? MethodGetAnalysisResult : method;

        try
        {
            var result = await _analysisService.GetAnalysisAsync(analysisGuid, cancellationToken);

            string formatted;
            string summary;

            if (string.Equals(effectiveMethod, MethodGetAnalysisSummary, StringComparison.Ordinal))
            {
                formatted = FormatSummary(result, analysisGuid);
                summary = $"Returned executive summary for analysis {analysisGuid}.";
            }
            else
            {
                formatted = FormatFullResult(result);
                summary = $"Returned full analysis result for analysis {analysisGuid}.";
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "AnalysisQueryHandler ({Correlation}) {Method} ok in {Duration}ms",
                correlationLogId, effectiveMethod, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new AnalysisQueryPayload
                {
                    Method = effectiveMethod,
                    AnalysisId = analysisGuid,
                    Content = formatted,
                    Found = true
                },
                summary: summary,
                confidence: 1.0,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ModelCalls = 0,
                    ModelName = null
                });
        }
        catch (KeyNotFoundException)
        {
            stopwatch.Stop();
            // Per ADR-015 we deliberately do NOT log the documentId beyond the deterministic ID
            // form supplied by the LLM/caller. The "not found" signal is OK to log at info level.
            _logger.LogInformation(
                "AnalysisQueryHandler ({Correlation}) {Method} not-found in {Duration}ms",
                correlationLogId, effectiveMethod, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new AnalysisQueryPayload
                {
                    Method = effectiveMethod,
                    AnalysisId = analysisGuid,
                    Message = $"Analysis '{documentId}' not found. The analysis may not exist or may have been removed.",
                    Found = false
                },
                summary: $"Analysis '{documentId}' not found.",
                confidence: 0.0,
                execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Argument parsing
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse playbook-context arguments from <see cref="AnalysisTool.Configuration"/> JSON.
    /// Expected shape: <c>{ "documentId": "&lt;guid&gt;", "method": "GetAnalysisResult" | "GetAnalysisSummary" }</c>.
    /// </summary>
    private static (string? DocumentId, string Method) ParsePlaybookArgs(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return (null, MethodGetAnalysisResult);

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, MethodGetAnalysisResult);

            var documentId = doc.RootElement.TryGetProperty("documentId", out var idProp)
                             && idProp.ValueKind == JsonValueKind.String
                ? idProp.GetString()
                : null;

            var method = doc.RootElement.TryGetProperty("method", out var methodProp)
                         && methodProp.ValueKind == JsonValueKind.String
                ? methodProp.GetString() ?? MethodGetAnalysisResult
                : MethodGetAnalysisResult;

            return (documentId, method);
        }
        catch (JsonException)
        {
            return (null, MethodGetAnalysisResult);
        }
    }

    /// <summary>
    /// Parse chat-context arguments from <see cref="ChatInvocationContext.ToolArgumentsJson"/>.
    /// The adapter validates schema at construction; we just project the documentId + method out.
    /// </summary>
    private static (string? DocumentId, string Method) ParseChatArgs(string? toolArgumentsJson)
    {
        if (string.IsNullOrWhiteSpace(toolArgumentsJson))
            return (null, MethodGetAnalysisResult);

        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, MethodGetAnalysisResult);

            var documentId = doc.RootElement.TryGetProperty("documentId", out var idProp)
                             && idProp.ValueKind == JsonValueKind.String
                ? idProp.GetString()
                : null;

            var method = doc.RootElement.TryGetProperty("method", out var methodProp)
                         && methodProp.ValueKind == JsonValueKind.String
                ? methodProp.GetString() ?? MethodGetAnalysisResult
                : MethodGetAnalysisResult;

            return (documentId, method);
        }
        catch (JsonException)
        {
            return (null, MethodGetAnalysisResult);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Formatting (preserved from the legacy AnalysisQueryTools shape so chat output
    // is unchanged for end users)
    // ─────────────────────────────────────────────────────────────────────────────

    private static string FormatFullResult(Sprk.Bff.Api.Api.Ai.AnalysisDetailResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Analysis Result: {result.DocumentName}");
        sb.AppendLine($"**Status**: {result.Status}");
        sb.AppendLine($"**Document ID**: {result.DocumentId}");

        if (result.StartedOn.HasValue)
        {
            sb.AppendLine($"**Analyzed**: {result.StartedOn.Value:yyyy-MM-dd HH:mm} UTC");
        }

        if (result.TokenUsage != null)
        {
            sb.AppendLine($"**Token Usage**: {result.TokenUsage.Input} input, {result.TokenUsage.Output} output");
        }

        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(result.WorkingDocument))
        {
            sb.AppendLine("### Analysis Output");
            sb.AppendLine(result.WorkingDocument);
        }
        else if (!string.IsNullOrWhiteSpace(result.FinalOutput))
        {
            sb.AppendLine("### Analysis Output");
            sb.AppendLine(result.FinalOutput);
        }
        else
        {
            sb.AppendLine("*No analysis output available.*");
        }

        return sb.ToString().TrimEnd();
    }

    private static readonly Regex ExecutiveSummaryRegex = new(
        @"(?:##?\s*(?:Executive\s+)?Summary[\s:]*\n)([\s\S]*?)(?=\n##|\z)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string FormatSummary(Sprk.Bff.Api.Api.Ai.AnalysisDetailResult result, Guid analysisGuid)
    {
        var output = result.WorkingDocument ?? result.FinalOutput;

        if (string.IsNullOrWhiteSpace(output))
        {
            return $"No summary available for analysis '{analysisGuid}'.";
        }

        var summaryMatch = ExecutiveSummaryRegex.Match(output);
        if (summaryMatch.Success)
        {
            var summaryText = summaryMatch.Groups[1].Value.Trim();
            return $"## Executive Summary: {result.DocumentName}\n\n{summaryText}";
        }

        var preview = output.Length > 600
            ? output[..600] + "\n\n*(Summary truncated — use method='GetAnalysisResult' for full output)*"
            : output;
        return $"## Summary: {result.DocumentName}\n\n{preview}";
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Error-result builders
    // ─────────────────────────────────────────────────────────────────────────────

    private ToolResult BuildCancelledResult(AnalysisTool tool, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "Analysis query was cancelled.",
            ToolErrorCodes.Cancelled,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    private ToolResult BuildInternalErrorResult(AnalysisTool tool, Exception ex, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            $"Analysis query failed: {ex.Message}",
            ToolErrorCodes.InternalError,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    // ─────────────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. Carries the
    /// method discriminator + the formatted text content the chat agent renders to the user.
    /// </summary>
    public sealed class AnalysisQueryPayload
    {
        /// <summary>Method that produced this payload — echo of the input discriminator.</summary>
        [JsonPropertyName("method")]
        public string Method { get; set; } = MethodGetAnalysisResult;

        /// <summary>Analysis GUID the caller requested (null if input was unparseable).</summary>
        [JsonPropertyName("analysisId")]
        public Guid? AnalysisId { get; set; }

        /// <summary>
        /// Formatted text content (markdown-style) when <see cref="Found"/> is true. Same
        /// shape as the legacy <c>AnalysisQueryTools</c> string output for behavioral parity.
        /// </summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>
        /// Human-readable status message — populated when <see cref="Found"/> is false
        /// (invalid id, analysis not found).
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>True when the analysis was located and content was returned.</summary>
        [JsonPropertyName("found")]
        public bool Found { get; set; }
    }
}
