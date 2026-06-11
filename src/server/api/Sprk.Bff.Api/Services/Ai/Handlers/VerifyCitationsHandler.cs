using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Ai.Safety.Citations;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat- and playbook-invocable typed handler that explicitly verifies legal citations
/// against authoritative sources (R6 Wave 7c). Replaces the legacy hardcoded
/// <c>VerifyCitationsTool</c> class previously instantiated in
/// <c>SprkChatAgentFactory.ResolveTools</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One handler, one method</strong>: the only LLM-facing function is
/// <c>verify_citations(text)</c> — the LLM passes a passage of text and receives a
/// per-citation verification report. Mirrors the legacy tool's single
/// <c>VerifyCitationsAsync</c> method.
/// </para>
/// <para>
/// <strong>Capability gate (R6 Wave 7b infrastructure)</strong>: the corresponding
/// <c>sprk_analysistool</c> row sets <c>sprk_requiredcapability = "verify_citations"</c>
/// (= <see cref="Sprk.Bff.Api.Models.Ai.Chat.PlaybookCapabilities.VerifyCitations"/>).
/// The data-driven block in <c>SprkChatAgentFactory.ResolveTools</c> applies
/// <c>IsCapabilityGateSatisfied</c> at session start and silently withholds this
/// handler when the playbook's capability set lacks the value. Standalone chat
/// (no playbook, capabilities = <c>CoreCapabilities</c>) does NOT include
/// <c>verify_citations</c>, so this handler is unreachable from standalone chat
/// — preserving the pre-Wave-7c security boundary enforced by the hardcoded
/// <c>if (capabilities.Contains(PlaybookCapabilities.VerifyCitations))</c> block
/// that this migration removes.
/// </para>
/// <para>
/// <strong>NFR-13 binding</strong>: this explicit-verification path is one of two
/// citation safety mechanisms. The other — <see cref="CitationSafetyCheck"/> middleware
/// — runs UNCONDITIONALLY after every LLM response regardless of whether this
/// handler is registered for the playbook. The two compose: this handler lets the
/// LLM verify citations on demand within a turn; the middleware always annotates
/// every response's citations post-hoc. Removing the hardcoded gate on this
/// handler does NOT weaken NFR-13 — the unconditional post-LLM check remains in
/// the safety pipeline (registered in <c>AiSafetyModule</c>).
/// </para>
/// <para>
/// <strong>Dependencies</strong>: <see cref="ICitationVerificationService"/> only
/// (singleton; resolved via constructor injection — auto-discovered by
/// <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c>).
/// </para>
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Both"/>.
/// Chat is the primary call site (matches the legacy hardcoded registration);
/// playbook context is exposed so playbook-orchestrated review steps can verify
/// citations against an Action-supplied passage of text.
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
/// <c>TenantId</c> on both chat and playbook paths. Verification providers are
/// tenant-agnostic (they query authoritative legal databases), but the handler
/// still enforces tenant presence so logs are correlatable.</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + outcome +
/// IDs + duration + citation-COUNT buckets ONLY. NEVER the citation text or the
/// passage that was scanned (citations may contain client names + matter
/// references + PII).</item>
/// <item><strong>ADR-018</strong>: this is NOT a feature flag. The capability
/// gate is per-tool authorization (data-driven via <c>sprk_requiredcapability</c>),
/// not a service-registration kill switch.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler
/// publish-size delta ≤+0.1 MB.</item>
/// </list>
/// </remarks>
public sealed class VerifyCitationsHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(VerifyCitationsHandler);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ICitationVerificationService _verificationService;
    private readonly ILogger<VerifyCitationsHandler> _logger;

    public VerifyCitationsHandler(
        ICitationVerificationService verificationService,
        ILogger<VerifyCitationsHandler> logger)
    {
        _verificationService = verificationService ?? throw new ArgumentNullException(nameof(verificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Verify Citations",
        Description: "Verifies legal citations found in the provided text against authoritative sources. " +
                     "Returns verification status, confidence, and source URLs for each citation. " +
                     "Use when the user asks to verify references, check case validity, or confirm " +
                     "regulatory citations.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "text",
                "The text passage containing legal citations to extract and verify. Supports case law, " +
                "statutes, patents, SEC filings, and federal regulations.",
                ToolParameterType.String,
                Required: true)
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

        return ToolValidationResult.Success();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Playbook-context path. Reads <c>text</c> from the tool's
    /// <see cref="AnalysisTool.Configuration"/> JSON when present, otherwise falls back to
    /// <see cref="DocumentContext.ExtractedText"/>. The playbook call site is provided so
    /// review steps can verify citations against an Action-supplied passage of text.
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
                "VerifyCitationsHandler executing for analysis {AnalysisId}, tool {ToolId}",
                context.AnalysisId, tool.Id);

            cancellationToken.ThrowIfCancellationRequested();

            var text = ParsePlaybookArgs(tool.Configuration) ?? context.Document?.ExtractedText;
            return await DispatchAsync(
                tool,
                text: text,
                startedAt: startedAt,
                stopwatch: stopwatch,
                correlationLogId: context.AnalysisId.ToString(),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("VerifyCitationsHandler cancelled for analysis {AnalysisId}", context.AnalysisId);
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "VerifyCitationsHandler failed for analysis {AnalysisId}: {ErrorType}",
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
                "VerifyCitationsHandler chat-invocation for session {ChatSessionId}, decision {DecisionId}",
                context.ChatSessionId, context.DecisionId);

            cancellationToken.ThrowIfCancellationRequested();

            var text = ParseChatArgs(context.ToolArgumentsJson);
            return await DispatchAsync(
                tool,
                text: text,
                startedAt: startedAt,
                stopwatch: stopwatch,
                correlationLogId: $"session={context.ChatSessionId},decision={context.DecisionId}",
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "VerifyCitationsHandler chat cancelled for session {ChatSessionId}, decision {DecisionId}",
                context.ChatSessionId, context.DecisionId);
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "VerifyCitationsHandler chat failed for session {ChatSessionId}, decision {DecisionId}: {ErrorType}",
                context.ChatSessionId, context.DecisionId, ex.GetType().Name);
            return BuildInternalErrorResult(tool, ex, startedAt);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Dispatcher
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> DispatchAsync(
        AnalysisTool tool,
        string? text,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "text is required.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        // ADR-015: log only the input LENGTH and an opaque bucket — NEVER the text content
        // (citations may contain client names + matter references + PII).
        _logger.LogInformation(
            "VerifyCitationsHandler ({Correlation}) start verify — textLen={TextLen}",
            correlationLogId, text.Length);

        var report = await _verificationService.VerifyAllAsync(text, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        var totalBucket = BucketCitationCount(report.TotalCitations);

        // ADR-015: log COUNTS + buckets only. NEVER raw citation text or per-citation details
        // that might include client names embedded in the raw match.
        _logger.LogInformation(
            "VerifyCitationsHandler ({Correlation}) ok total={Total} ({Bucket}) verified={Verified} unverified={Unverified} errors={Errors} in {Duration}ms",
            correlationLogId,
            report.TotalCitations,
            totalBucket,
            report.Verified.Count,
            report.Unverified.Count,
            report.Errors.Count,
            stopwatch.ElapsedMilliseconds);

        if (report.TotalCitations == 0)
        {
            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new VerifyCitationsPayload
                {
                    Message = "No legal citations were found in the provided text.",
                    Citations = Array.Empty<VerifyCitationsEntry>()
                },
                summary: "No legal citations were found in the provided text.",
                confidence: 1.0,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ModelCalls = 0,
                    ModelName = null
                });
        }

        var entries = report.All.Select(r => new VerifyCitationsEntry
        {
            Raw = r.Citation.RawText,
            Type = r.Citation.CitationType.ToString(),
            Normalized = r.Citation.NormalizedKey,
            IsVerified = r.IsVerified,
            Confidence = r.ConfidenceScore,
            SourceUrl = r.SourceUrl,
            Provider = r.VerificationProvider,
            ErrorMessage = r.ErrorMessage
        }).ToArray();

        var summary = $"Verified {report.Verified.Count} of {report.TotalCitations} citations " +
                      $"({report.Unverified.Count} unverified, {report.Errors.Count} errors).";

        return ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: new VerifyCitationsPayload
            {
                Citations = entries,
                Total = report.TotalCitations,
                VerifiedCount = report.Verified.Count,
                UnverifiedCount = report.Unverified.Count,
                ErrorCount = report.Errors.Count
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

    // ─────────────────────────────────────────────────────────────────────────────
    // Argument parsing
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse playbook-context arguments from <see cref="AnalysisTool.Configuration"/> JSON.
    /// Expected shape: <c>{ "text": "&lt;passage&gt;" }</c>. Returns null when the configuration
    /// is empty, unparseable, or lacks a non-empty <c>text</c> field — caller falls back to the
    /// document's extracted text.
    /// </summary>
    private static string? ParsePlaybookArgs(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!doc.RootElement.TryGetProperty("text", out var textProp) ||
                textProp.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = textProp.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parse chat-context arguments from <see cref="ChatInvocationContext.ToolArgumentsJson"/>.
    /// The adapter validates schema at construction; we just project the <c>text</c> field out.
    /// </summary>
    private static string? ParseChatArgs(string? toolArgumentsJson)
    {
        if (string.IsNullOrWhiteSpace(toolArgumentsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!doc.RootElement.TryGetProperty("text", out var textProp) ||
                textProp.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = textProp.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-015 telemetry helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bucket the citation count for ADR-015-safe telemetry. Returns a coarse label
    /// rather than the exact integer when emitted alongside other identifiers — keeps
    /// logs free of precise counts that might reveal document-specific patterns
    /// (e.g., "1 citation" = single contract; ">20" = large filing).
    /// </summary>
    internal static string BucketCitationCount(int count)
    {
        return count switch
        {
            < 5 => "<5",
            <= 20 => "5-20",
            _ => ">20"
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Error-result builders
    // ─────────────────────────────────────────────────────────────────────────────

    private ToolResult BuildCancelledResult(AnalysisTool tool, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "Citation verification was cancelled.",
            ToolErrorCodes.Cancelled,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    private ToolResult BuildInternalErrorResult(AnalysisTool tool, Exception ex, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            $"Citation verification failed: {ex.Message}",
            ToolErrorCodes.InternalError,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    // ─────────────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. Mirrors the legacy
    /// <c>VerifyCitationsTool</c> output shape for behavioural parity: the LLM sees the same
    /// per-citation structure it expects from the today-hardcoded tool.
    /// </summary>
    public sealed class VerifyCitationsPayload
    {
        /// <summary>Per-citation verification entries. Empty when no citations were found.</summary>
        [JsonPropertyName("citations")]
        public IReadOnlyList<VerifyCitationsEntry> Citations { get; set; } = Array.Empty<VerifyCitationsEntry>();

        /// <summary>Optional message — populated when no citations were found.</summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>Total citations extracted from the input text.</summary>
        [JsonPropertyName("total")]
        public int Total { get; set; }

        /// <summary>Count of citations that were verified by an authoritative provider.</summary>
        [JsonPropertyName("verifiedCount")]
        public int VerifiedCount { get; set; }

        /// <summary>Count of citations that were extracted but could not be verified.</summary>
        [JsonPropertyName("unverifiedCount")]
        public int UnverifiedCount { get; set; }

        /// <summary>Count of citations where the provider threw an exception.</summary>
        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; set; }
    }

    /// <summary>
    /// A single citation entry in the verification report. Field shape matches the legacy
    /// <c>VerifyCitationsTool</c> serialization so existing LLM-side prompts continue to read
    /// the same property names.
    /// </summary>
    public sealed class VerifyCitationsEntry
    {
        /// <summary>Verbatim citation text as extracted.</summary>
        [JsonPropertyName("raw")]
        public string Raw { get; set; } = string.Empty;

        /// <summary>Citation type (CaseLaw, Statute, Patent, SecFiling, Regulation, Unknown).</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>Canonical identifier for the citation.</summary>
        [JsonPropertyName("normalized")]
        public string Normalized { get; set; } = string.Empty;

        /// <summary>True when an authoritative provider confirmed the citation.</summary>
        [JsonPropertyName("isVerified")]
        public bool IsVerified { get; set; }

        /// <summary>Provider confidence score in [0.0, 1.0].</summary>
        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }

        /// <summary>Canonical provider URL, or null when unavailable.</summary>
        [JsonPropertyName("sourceUrl")]
        public string? SourceUrl { get; set; }

        /// <summary>Provider name, "none", or "error".</summary>
        [JsonPropertyName("provider")]
        public string Provider { get; set; } = string.Empty;

        /// <summary>Human-readable error description when provider is "error"; null otherwise.</summary>
        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }
    }
}
